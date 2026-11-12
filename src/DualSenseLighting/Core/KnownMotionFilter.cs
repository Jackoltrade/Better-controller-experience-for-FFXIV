// SPDX-License-Identifier: GPL-3.0-only
#nullable enable
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace DualSenseLighting.Core;

// Matches a short sequence against a fixed bank. It does not learn playback lifetimes.
internal sealed partial class KnownMotionFilter
{
    private const int SourceCapacity = 32, CandidateCapacity = 128;
    private const float Floor = .000001f;
    private sealed class Pattern
    {
        internal readonly bool Position, Loop;
        internal readonly double Seconds;
        internal readonly Vector3 Tolerance, Minimum, Maximum;
        private readonly float[]? x;
        private readonly float[] y;
        internal int Segments => y.Length - 1;
        internal Pattern(bool position, double seconds, bool loop, float[]? x, float[] y)
        {
            ArgumentNullException.ThrowIfNull(y);
            if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 12 || y.Length is < 2 or > 513 ||
                x != null && x.Length != y.Length) throw new ArgumentException("Invalid known motion profile bounds.");
            var peakX = 0f; var peakY = 0f;
            var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0);
            var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0);
            for (var i = 0; i < y.Length; i++)
            {
                if (!float.IsFinite(y[i]) || x != null && !float.IsFinite(x[i]))
                    throw new ArgumentException("Known motion profiles must be finite.");
                peakX = Math.Max(peakX, Math.Abs(x?[i] ?? 0)); peakY = Math.Max(peakY, Math.Abs(y[i]));
                var value = new Vector3(x?[i] ?? 0, y[i], 0);
                minimum = Vector3.Min(minimum, value); maximum = Vector3.Max(maximum, value);
            }
            if (Math.Max(peakX, peakY) <= Floor) throw new ArgumentException("A known motion profile must contain motion.");
            Position = position; Seconds = seconds; Loop = loop;
            this.x = x == null ? null : (float[])x.Clone(); this.y = (float[])y.Clone();
            Tolerance = new(peakX * .05f + Floor, peakY * .05f + Floor, Floor);
            Minimum = minimum - Tolerance; Maximum = maximum + Tolerance;
        }
        internal Vector3 At(double phase)
        {
            if (!double.IsFinite(phase)) return new(float.NaN);
            phase = Loop ? phase - Math.Floor(phase) : Math.Clamp(phase, 0, 1);
            var offset = phase * Segments; var index = Math.Min(Segments - 1, (int)offset);
            var fraction = (float)(offset - index);
            return new((x?[index] ?? 0) + ((x?[index + 1] ?? 0) - (x?[index] ?? 0)) * fraction,
                y[index] + (y[index + 1] - y[index]) * fraction, 0);
        }
        internal bool Matches(Vector3 value, double phase)
        {
            var error = Vector3.Abs(value - At(phase));
            return error.X <= Tolerance.X && error.Y <= Tolerance.Y && error.Z <= Tolerance.Z;
        }
        internal bool Project(Vector3 value, int segment, out double phase)
        {
            var a = At(segment / (double)Segments);
            var b = At((segment + 1.0) / Segments);
            var direction = (b - a) / Tolerance;
            var relative = (value - a) / Tolerance;
            var length = direction.LengthSquared();
            var fraction = length > 0 ? Math.Clamp(Vector3.Dot(relative, direction) / length, 0, 1) : 0;
            phase = (segment + fraction) / Segments;
            return Matches(value, phase);
        }
        internal bool MatchTimed(Vector3 value, ref double phase, double slack)
        {
            if (Matches(value, phase)) return true;
            if (slack <= 0) return false;
            var wrapped = Loop ? phase - Math.Floor(phase) : phase;
            var center = Math.Min(Segments - 1, (int)(wrapped * Segments));
            for (var neighbor = 0; neighbor < 3; neighbor++)
            {
                var segment = center + (neighbor == 0 ? 0 : neighbor == 1 ? -1 : 1);
                if (Loop) segment = (segment + Segments) % Segments;
                else if (segment < 0 || segment >= Segments) continue;
                if (!Project(value, segment, out var projected)) continue;
                if (Loop) projected += Math.Round(phase - projected);
                var aligned = Math.Clamp(projected, phase - slack, phase + slack);
                if (!Matches(value, aligned)) continue;
                phase = aligned;
                return true;
            }
            return false;
        }
    }
    private struct Candidate
    {
        internal int Pattern, Hits;
        internal double Phase;
    }
    private sealed class Track
    {
        internal readonly Candidate[] Candidates = new Candidate[CandidateCapacity];
        internal int Count;
        internal bool Observed, Known, PhaseReset, Provisional = true;
        internal long Clock;
        internal Vector3 Value;
        internal void Clear() { Count = 0; Observed = Known = PhaseReset = false; Provisional = true; Clock = 0; Value = default; }
    }
    private sealed class Source
    {
        internal bool Used;
        internal long Address, Context, Config;
        internal int Slot;
        internal readonly Track Position = new(), Rotation = new();
    }
    private readonly object sync = new();
    private readonly Source[] sources = new Source[SourceCapacity];
    private readonly int[,] latest = new int[2, 2];
    private readonly Pattern[] patterns;
    private readonly long frequency, gap;
    private bool clockSeen;
    private long lastClock;
    private int invalid;

    internal KnownMotionFilter(long frequency = 0) : this(frequency, Patterns) { }
    private KnownMotionFilter(long frequency, Pattern[] patterns)
    {
        if (frequency < 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (patterns.Length is < 1 or > 32) throw new ArgumentException("Invalid known motion bank size.");
        this.frequency = frequency == 0 ? Stopwatch.Frequency : frequency;
        gap = Math.Max(1, this.frequency / 5); this.patterns = (Pattern[])patterns.Clone();
        for (var i = 0; i < sources.Length; i++) sources[i] = new Source();
        Clear();
    }
    private void Clear()
    {
        // Every observation enters under this lock and sets clockSeen first.
        // After a clear, repeated inactive invalidations need no track traversal.
        if (clockSeen)
            foreach (var source in sources) { source.Used = false; source.Position.Clear(); source.Rotation.Clear(); }
        for (var channel = 0; channel < 2; channel++) for (var slot = 0; slot < 2; slot++) latest[channel, slot] = -1;
        clockSeen = false;
    }
    internal void Invalidate()
    {
        Interlocked.Exchange(ref invalid, 1);
        if (!Monitor.TryEnter(sync)) return;
        try { Clear(); Interlocked.Exchange(ref invalid, 0); }
        finally { Monitor.Exit(sync); }
    }
    private bool Enter(long clock)
    {
        if (!Monitor.TryEnter(sync)) { Interlocked.Exchange(ref invalid, 1); return false; }
        if (Interlocked.Exchange(ref invalid, 0) != 0 || clockSeen && clock < lastClock) Clear();
        lastClock = clock; clockSeen = true; return true;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal void Realign(long source, long context, long config, long clock)
    {
        if (!Enter(clock)) return;
        try
        {
            // Native loop resets can jump the authored phase without a matching
            // wall-clock interval. Discard only its old phase hypotheses; the
            // next sample must still fit a fixed profile. This is not a birth ID.
            foreach (var entry in sources)
            {
                if (!entry.Used || entry.Address != source || entry.Context != context || entry.Config != config) continue;
                // The last written vector is still current until another setter
                // arrives; do not turn it into a one-frame reset pulse.
                entry.Position.PhaseReset = entry.Rotation.PhaseReset = true;
            }
        }
        finally { Monitor.Exit(sync); }
    }

    internal void Observe(SecondaryVectorSample value, bool position, long clock)
    {
        if (!Enter(clock)) return;
        try
        {
            var vector = new Vector3(value.X, value.Y, value.Z);
            if (!value.Scoped || value.Source == 0 || value.Context == 0 || value.Config == 0 ||
                value.Slot is < 0 or > 1 || !Finite(vector)) { Clear(); return; }
            var index = -1; var free = -1;
            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (!source.Used) { if (free < 0) free = i; continue; }
                if (source.Address == value.Source && source.Context == value.Context &&
                    source.Config == value.Config && source.Slot == value.Slot) { index = i; break; }
            }
            if (index < 0)
            {
                if (free < 0) { Clear(); return; }
                index = free; var source = sources[index]; source.Used = true;
                source.Address = value.Source; source.Context = value.Context; source.Config = value.Config; source.Slot = value.Slot;
            }
            var track = position ? sources[index].Position : sources[index].Rotation;
            if (track.Observed && clock - track.Clock > gap) track.Clear();
            if (track.PhaseReset)
            { track.Count = 0; track.Provisional = true; track.PhaseReset = false; }
            var kept = 0; track.Known = false;
            for (var i = 0; i < track.Count; i++)
            {
                var candidate = track.Candidates[i]; var pattern = patterns[candidate.Pattern];
                var advance = (clock - track.Clock) / (frequency * pattern.Seconds);
                var phase = candidate.Phase + advance;
                // Native evaluation and callback clocks can differ slightly.
                // Correct at most 10% of this update's advance without loosening
                // amplitude bounds or accepting a large tempo jump.
                if (!pattern.Loop && phase > 1 || !pattern.MatchTimed(vector, ref phase, advance * .1)) continue;
                candidate.Phase = pattern.Loop ? phase - Math.Floor(phase) : phase;
                if (clock > track.Clock) candidate.Hits = Math.Min(3, candidate.Hits + 1);
                track.Known |= candidate.Hits >= 3;
                track.Candidates[kept++] = candidate;
            }
            // A first plausible vehicle sample stays quiet during confirmation.
            // Once its trajectory disagrees, reseeding must not repeatedly hide
            // unrelated motion just because each point fits some profile phase.
            if (track.Count > 0 && kept == 0) track.Provisional = false;
            track.Count = kept;
            if (kept == 0 && vector != Vector3.Zero) Seed(track, vector, position);
            if (track.Count == 0 && vector != Vector3.Zero) track.Provisional = false;
            track.Observed = true; track.Clock = clock; track.Value = vector;
            latest[position ? 0 : 1, value.Slot] = index;
        }
        finally { Monitor.Exit(sync); }
    }
    private void Seed(Track track, Vector3 value, bool position)
    {
        for (var p = 0; p < patterns.Length; p++)
        {
            var pattern = patterns[p];
            if (pattern.Position != position || value.X < pattern.Minimum.X || value.X > pattern.Maximum.X ||
                value.Y < pattern.Minimum.Y || value.Y > pattern.Maximum.Y ||
                value.Z < pattern.Minimum.Z || value.Z > pattern.Maximum.Z) continue;
            for (var segment = 0; segment < pattern.Segments; segment++)
            {
                if (!pattern.Project(value, segment, out var phase)) continue;
                if (pattern.Loop && phase == 1) phase = 0;
                var duplicate = false;
                for (var i = 0; i < track.Count; i++)
                    if (track.Candidates[i].Pattern == p && Math.Abs(track.Candidates[i].Phase - phase) < .000001)
                    { duplicate = true; break; }
                if (duplicate) continue;
                if (track.Count == CandidateCapacity) { track.Count = 0; return; }
                track.Candidates[track.Count++] = new Candidate { Pattern = p, Phase = phase, Hits = 1 };
            }
        }
    }

    internal float GetGain(Vector3 position, Vector3 rotation, long now)
    {
        if (!Enter(now)) return 1;
        try
        {
            if (!Finite(position) || !Finite(rotation)) { Clear(); return 1; }
            var observedPosition = Vector3.Zero; var observedRotation = Vector3.Zero;
            var contributing = false; var allKnown = true;
            for (var channel = 0; channel < 2; channel++) for (var slot = 0; slot < 2; slot++)
            {
                var index = latest[channel, slot]; if (index < 0) continue;
                var track = channel == 0 ? sources[index].Position : sources[index].Rotation;
                if (track.Value == Vector3.Zero) continue;
                if (now - track.Clock > gap) { Clear(); return 1; }
                if (channel == 0) observedPosition += track.Value; else observedRotation += track.Value;
                contributing = true; allKnown &= track.Known || track.Provisional && track.Count > 0;
            }
            if (observedPosition != position || observedRotation != rotation) { Clear(); return 1; }
            return contributing && allKnown ? 0 : 1;
        }
        finally { Monitor.Exit(sync); }
    }
}
