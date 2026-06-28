using NAudio.Wave;
using System;
using NCalc;

namespace F4CE.Objects;

internal partial class OSampleProviderOne : ISampleProvider
{
	private readonly ISampleProvider Source;

	public FPlaybackSettings PlaybackSettings { get; set; }
	public WaveFormat WaveFormat => Source.WaveFormat;

	public OSampleProviderOne(ISampleProvider InSource)
	{
		Source = InSource;

		if (WaveFormat.Channels != 2)
		{
			throw new InvalidOperationException("Stereo output required");
		}
	}

	public int Read(float[] Buffer, int Offset, int Count)
	{
		long TrimStart = AlignedTrimStart;
		long TrimEnd = AlignedTrimEnd;

		if (TrimStart > 0)
		{
			long PlaybackBase = SourcePosition - SpeedBufferCount;
			if (PlaybackBase < TrimStart)
			{
				SkipSourceSamples(TrimStart - PlaybackBase);
			}
		}

		if (TrimEnd >= 0 && SourcePosition - SpeedBufferCount >= TrimEnd)
		{
			return 0;
		}

		int Read;
		if (PlaybackSettings.PlaybackSpeed == 1f)
		{
			if (TrimEnd >= 0)
			{
				long Remaining = TrimEnd - SourcePosition;
				if (Remaining <= 0)
				{
					return 0;
				}

				Count = (int)Math.Min(Count, Remaining);
				Count -= Count % WaveFormat.Channels;
				if (Count <= 0)
				{
					return 0;
				}
			}
			Read = ReadFromSource(Buffer, Offset, Count);
		}
		else
		{
			Read = ReadWithPlaybackSpeed(Buffer, Offset, Count);
		}

		if (PlaybackSettings.Raw)
		{
			return Read;
		}

		return ProcessBuffer(Buffer, Offset, Read);
	}

	private bool FillSpeedBuffer(int MinimumSamples)
	{
		CompactSpeedBuffer();

		while (SpeedBufferCount < MinimumSamples)
		{
			EnsureSpeedBufferCapacity(SpeedBufferCount + MinimumSamples);

			int Available = SpeedBuffer.Length - SpeedBufferCount;

			long TrimEnd = AlignedTrimEnd;
			if (TrimEnd >= 0)
			{
				long Remaining = TrimEnd - SourcePosition;
				if (Remaining <= 0)
				{
					return SpeedBufferCount >= MinimumSamples;
				}

				Available = (int)Math.Min(Available, Remaining);
			}

			int Read = ReadFromSource(SpeedBuffer, SpeedBufferCount, Available);
			if (Read == 0)
			{
				return SpeedBufferCount >= MinimumSamples;
			}

			SpeedBufferCount += Read;
		}

		return true;
	}

	private float Phase;
	private float PanPhase;

	private int ProcessBuffer(float[] Buffer, int Offset, int Read)
	{
		float SampleRate = WaveFormat.SampleRate;

		for (int ReadIndex = 0; ReadIndex < Read; ReadIndex += 2)
		{
			float PitchScale = MathF.Pow(2f, PlaybackSettings.TransposeSemitones / 12f);
			float Frequency = Buffer[Offset + ReadIndex] * PitchScale;
			float Sample = EvaluateWave(Frequency, Phase);

			float RFactor = 0.5f;
			for (int RIndex = PlaybackSettings.Rs; RIndex > 0; --RIndex)
			{
				Sample += Sample * (RFactor / RIndex);
			}

			float Pan = MathF.Sin(2f * MathF.PI * PlaybackSettings.PanSpeed * PanPhase);

			float LeftGain = MathF.Min((1f - Pan) * 0.5f + PlaybackSettings.PanBaseVolume, 1f);
			float RightGain = MathF.Min((1f + Pan) * 0.5f + PlaybackSettings.PanBaseVolume, 1f);

			Buffer[Offset + ReadIndex] = Sample * LeftGain * PlaybackSettings.Loudness * PlaybackSettings.LeftLoundness;
			Buffer[Offset + ReadIndex + 1] = Sample * RightGain * PlaybackSettings.Loudness * PlaybackSettings.RightLoundness;

			Phase += 1f / SampleRate;
			PanPhase += 1f / SampleRate;
		}

		return Read;
	}

	private long SourcePosition;
	private float SpeedPosition;
	private float[] SpeedBuffer = [];
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

			int NextConsumedFrames = (int) (SpeedPosition + PlaybackSettings.PlaybackSpeed);
			int RequiredSamples = Math.Max(Frame1 + 1, NextConsumedFrames) * Channels;

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
			SpeedPosition += PlaybackSettings.PlaybackSpeed;

			int ConsumedFrames = (int)SpeedPosition;
			if (ConsumedFrames > 0)
			{
				int ConsumedSamples = ConsumedFrames * Channels;

				SpeedPosition -= ConsumedFrames;
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

	private void EnsureSpeedBufferCapacity(int Capacity)
	{
		if (SpeedBuffer.Length >= Capacity)
		{
			return;
		}

		int NewCapacity = Math.Max(Capacity, Math.Max(1024, SpeedBuffer.Length * 2));
		Array.Resize(ref SpeedBuffer, NewCapacity);
	}

	private long AlignedTrimStart
	{
		get
		{
			long v = PlaybackSettings.TrimStart;
			return v - (v % WaveFormat.Channels);
		}
	}

	private long AlignedTrimEnd
	{
		get
		{
			long v = PlaybackSettings.TrimEnd;
			if (v < 0) return v; 
			return v - (v % WaveFormat.Channels);
		}
	}

	private int ReadFromSource(float[] Buffer, int Offset, int Count)
	{
		int Read = Source.Read(Buffer, Offset, Count);
		SourcePosition += Read;
		return Read;
	}

	private void SkipSourceSamples(long SamplesToSkip)
	{
		if (SpeedBufferCount > 0)
		{
			int FromBuffer = (int)Math.Min(SpeedBufferCount, SamplesToSkip);
			SpeedBufferStart += FromBuffer;
			SpeedBufferCount -= FromBuffer;
			SamplesToSkip -= FromBuffer;
		}

		float[] Temp = new float[4096];
		while (SamplesToSkip > 0)
		{
			int ToRead = (int)Math.Min(Temp.Length, SamplesToSkip);
			int Read = Source.Read(Temp, 0, ToRead);
			if (Read == 0)
			{
				break;
			}

			SourcePosition += Read;
			SamplesToSkip -= Read;
		}
	}
}