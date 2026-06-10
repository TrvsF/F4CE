using NAudio.Wave;
using System;
using NCalc;

namespace F4CE.Objects;
internal partial class OFrequencyShiftSampleProvider : ISampleProvider
{
	private readonly ISampleProvider Source;

	public long Duration { get; init; }

	public string WaveExpression { get; set; } = "f*t";
	public bool Raw { get; set; } = true;
	public float TransposeSemitones { get; set; } = 0f;
	public float PlaybackSpeed { get; set; } = 1f;
	public float PanSpeed { get; set; } = 2f;
	public int Rs { get; set; } = 6;

	public WaveFormat WaveFormat => Source.WaveFormat;

	public OFrequencyShiftSampleProvider(ISampleProvider InSource)
	{
		Source = InSource;

		if (WaveFormat.Channels != 2)
		{
			throw new InvalidOperationException("Stereo output required");
		}
	}

	public int Read(float[] Buffer, int Offset, int Count)
	{
		int Read;

		if (PlaybackSpeed == 1f)
		{
			Read = Source.Read(Buffer, Offset, Count);
		}
		else
		{
			Read = ReadWithPlaybackSpeed(Buffer, Offset, Count);
		}

		if (Raw)
		{
			return Read;
		}

		return ProcessBuffer(Buffer, Offset, Read);
	}

	private float Phase;
	private float PanPhase;

	private int ProcessBuffer(float[] Buffer, int Offset, int Read)
	{
		float SampleRate = WaveFormat.SampleRate;

		for (int ReadIndex = 0; ReadIndex < Read; ReadIndex += 2)
		{
			float PitchScale = MathF.Pow(2f, TransposeSemitones / 12f);
			float Frequency = Buffer[Offset + ReadIndex] * PitchScale;
			float Sample = EvaluateWave(Frequency, Phase);

			float RFactor = 0.5f;
			for (int RIndex = Rs; RIndex > 0; --RIndex)
			{
				Sample += Sample * (RFactor / RIndex);
			}

			float Pan = MathF.Sin(2f * MathF.PI * PanSpeed * PanPhase);

			float LeftGain = (1f - Pan) * 0.5f;
			float RightGain = (1f + Pan) * 0.5f;

			Buffer[Offset + ReadIndex] = Sample * LeftGain;
			Buffer[Offset + ReadIndex + 1] = Sample * RightGain;

			Phase += 1f / SampleRate;
			PanPhase += 1f / SampleRate;
		}

		return Read;
	}

	private float[] SpeedBuffer = Array.Empty<float>();
	private float SpeedPosition;
	private int SpeedBufferStart;
	private int SpeedBufferCount;

	private int ReadWithPlaybackSpeed(float[] Buffer, int Offset, int Count)
	{
		int Channels = WaveFormat.Channels;
		int OutputSamples = 0;

		while (OutputSamples + Channels <= Count)
		{
			int Frame0 = (int)SpeedPosition;
			int Frame1 = Frame0 + 1;

			int RequiredSamples = (Frame1 + 1) * Channels + (2); // a lil on the side

			if (!FillSpeedBuffer(RequiredSamples))
			{
				break;
			}

			int Sample0 = SpeedBufferStart + (Frame0 * Channels);
			int Sample1 = SpeedBufferStart + (Frame1 * Channels);
			float Alpha = SpeedPosition - Frame0;

			for (int Channel = 0; Channel < Channels; ++Channel)
			{
				float A = SpeedBuffer[Sample0 + Channel];
				float B = SpeedBuffer[Sample1 + Channel];
				Buffer[Offset + OutputSamples + Channel] = A + ((B - A) * Alpha);
			}

			OutputSamples += Channels;
			SpeedPosition += PlaybackSpeed;

			int ConsumedFrames = (int)SpeedPosition;

			if (ConsumedFrames > 0)
			{
				SpeedPosition -= ConsumedFrames;
				int ConsumedSamples = ConsumedFrames * Channels;

				SpeedBufferStart += ConsumedSamples;
				SpeedBufferCount -= ConsumedSamples;
			}
		}

		return OutputSamples;
	}

	private void CompactSpeedBuffer()
	{
		if (SpeedBufferStart == 0)
		{
			return;
		}

		Array.Copy(SpeedBuffer, SpeedBufferStart, SpeedBuffer, 0, SpeedBufferCount);
		SpeedBufferStart = 0;
	}

	private bool FillSpeedBuffer(int MinimumSamples)
	{
		CompactSpeedBuffer();

		while (SpeedBufferCount < MinimumSamples)
		{
			EnsureSpeedBufferCapacity(SpeedBufferCount + MinimumSamples);

			int Read = Source.Read(SpeedBuffer, SpeedBufferCount, SpeedBuffer.Length - SpeedBufferCount);
			if (Read == 0)
			{
				return SpeedBufferCount >= MinimumSamples;
			}

			SpeedBufferCount += Read;
		}

		return true;
	}

	private void EnsureSpeedBufferCapacity(int Capacity)
	{
		if (SpeedBuffer.Length >= Capacity)
		{
			return;
		}

		int NewCapacity = Math.Max(Capacity, Math.Max(1024, SpeedBuffer.Length * 2));
		Array.Resize(ref SpeedBuffer, NewCapacity);
	}
}