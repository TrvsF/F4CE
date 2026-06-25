using F4CE.Objects;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace F4CE;

internal class PlaybackManager
{
	public static readonly int SampleRate = 44100;
	public static readonly int Channels = 2;
	public static readonly int BitRate = SampleRate * Channels;
	public static readonly float BitRatePerMillisecond = BitRate / 1000f;

	public static readonly List<OAudioPlayback> ActivePlaybacks = new();
	public static readonly List<OAudioPlayback> StoredPlaybacks = new();

	private static readonly string SavePath = Path.Combine(AppContext.BaseDirectory, "playbacks.bin");

	public static void RefreshPlaybacks()
	{
		CreateBasePlaybacks();
		StoredPlaybacks.AddRange(LoadListFromFile(SavePath));
	}

	public static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
	private const int SerializationVersion = 1;

	public static void SaveListToFile()
	{
		string? Directory = Path.GetDirectoryName(SavePath);
		if (!string.IsNullOrEmpty(Directory))
		{
			System.IO.Directory.CreateDirectory(Directory);
		}

		using FileStream Fs = new(SavePath, FileMode.Create, FileAccess.Write);
		using BinaryWriter Bw = new(Fs);

		Bw.Write(SerializationVersion);
		Bw.Write(StoredPlaybacks.Count);

		foreach (OAudioPlayback Playback in StoredPlaybacks)
		{
			Playback.WritePlayback(Bw);
		}
	}

	public static List<OAudioPlayback> LoadListFromFile(string FilePath)
	{
		if (!File.Exists(FilePath))
		{
			return [];
		}

		using FileStream FileStream = new(FilePath, FileMode.Open, FileAccess.Read);
		using BinaryReader BinaryReader = new(FileStream);

		int Version = BinaryReader.ReadInt32();
		if (Version != SerializationVersion)
		{
			throw new InvalidDataException($"Save file version {Version} is not supported by this build (expected {SerializationVersion}).");
		}

		int ReadCount = BinaryReader.ReadInt32();
		List<OAudioPlayback> Result = new(ReadCount);

		for (int ReadIndex = 0; ReadIndex < ReadCount; ++ReadIndex)
		{
			Result.Add(ReadPlayback(BinaryReader, IsChild: false));
		}

		return Result;
	}

	private static OAudioPlayback ReadPlayback(BinaryReader Br, bool IsChild)
	{
		OAudioPlayback Playback = new() 
		{ 
			IsChild = IsChild
		};
		
		Playback.ReadPlayback(Br);		
		return Playback;
	}

	public static void AddPlayback(OAudioPlayback AudioPlayback)
	{
		ActivePlaybacks.Add(AudioPlayback);
	}

	public static void RemovePlayback(OAudioPlayback AudioPlayback)
	{
		if (!ActivePlaybacks.Contains(AudioPlayback))
		{
			return;
		}

		ActivePlaybacks.RemoveAt(ActivePlaybacks.IndexOf(AudioPlayback));
	}

	private static void CreateBasePlaybacks(int PlaybackCount = 4)
	{
		for (int Playback = 0; Playback < PlaybackCount; ++Playback)
		{
			ActivePlaybacks.Add(new());
		}
	}

	public static void ExportProviders(List<ISampleProvider> RenderedProviders)
	{
		if (RenderedProviders.Count == 0) return;

		MixingSampleProvider Mixer = new(RenderedProviders)
		{
			ReadFully = false
		};

		using WaveFileWriter Writer = new(GetPath(), Mixer.WaveFormat);
		float[] Buffer = new float[4096];
		int Read;
		while ((Read = Mixer.Read(Buffer, 0, Buffer.Length)) > 0)
		{
			Writer.WriteSamples(Buffer, 0, Read);
		}
	}

	public static string GetPath()
	{
		string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		string Path = System.IO.Path.Combine(DesktopPath, "F4CE.wav");

		int SaveCounter = 1337;
		while (File.Exists(Path))
		{
			Path = System.IO.Path.Combine(DesktopPath, $"F4CE-{SaveCounter}.wav");
			++SaveCounter;
		}

		return Path;
	}

	public static void SaveAllPlaybacksToFile()
	{
		if (ActivePlaybacks.Count == 0)
		{
			return;
		}

		List<ISampleProvider> RenderedProviders = new();

		foreach (var Playback in ActivePlaybacks)
		{
			if (!Playback.HasRecording)
			{
				continue;
			}

			Playback.ExportProvider(out var Export);
			RenderedProviders.AddRange(Export);
		}

		ExportProviders(RenderedProviders);
	}
}
