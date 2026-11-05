using System.Media;
using System.Text;

namespace ReminderBubble.Services;

/// <summary>A soft three-note chime synthesised in memory (no audio asset to ship).</summary>
public static class Chime
{
    static SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            _player ??= new SoundPlayer(new MemoryStream(Build()));
            _player.Play();
        }
        catch (Exception ex) { Log.Write("Chime: " + ex.Message); }
    }

    static byte[] Build()
    {
        const int rate = 44100;
        const double seconds = 1.7;
        var samples = new double[(int)(rate * seconds)];

        // (frequency Hz, start s, gain) - a rising G major arpeggio
        (double f, double t0, double g)[] notes = { (783.99, 0.00, 0.55), (1174.66, 0.13, 0.50), (1567.98, 0.27, 0.42) };
        double[] partials = { 1.0, 2.76, 5.40 };
        double[] gains = { 1.0, 0.30, 0.10 };
        double[] decays = { 3.4, 7.0, 11.0 };

        foreach (var (f, t0, g) in notes)
        {
            var start = (int)(t0 * rate);
            for (var i = start; i < samples.Length; i++)
            {
                var t = (i - start) / (double)rate;
                var attack = Math.Min(1.0, t / 0.006);
                var s = 0.0;
                for (var p = 0; p < partials.Length; p++)
                    s += gains[p] * Math.Exp(-t * decays[p]) * Math.Sin(2 * Math.PI * f * partials[p] * t);
                samples[i] += s * g * attack;
            }
        }

        var peak = samples.Max(Math.Abs);
        var scale = peak > 0 ? 0.65 / peak : 1.0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        var dataLen = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * scale * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
