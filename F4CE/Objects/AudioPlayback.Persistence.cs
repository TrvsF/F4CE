using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace F4CE.Objects;

internal partial class OAudioPlayback
{
	private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
	private const int SerializationVersion = 1;

	public static void SaveListToFile(IReadOnlyList<OAudioPlayback> Playbacks, string FilePath)
	{
		CachedList = null;

		string? Directory = Path.GetDirectoryName(FilePath);
		if (!string.IsNullOrEmpty(Directory))
			System.IO.Directory.CreateDirectory(Directory);

		using FileStream Fs = new(FilePath, FileMode.Create, FileAccess.Write);
		using BinaryWriter Bw = new(Fs);

		Bw.Write(SerializationVersion);
		Bw.Write(Playbacks.Count);

		foreach (OAudioPlayback Playback in Playbacks)
			WritePlayback(Bw, Playback);
	}

	private static List<OAudioPlayback>? CachedList = null;
	public static List<OAudioPlayback> LoadListFromFile(string FilePath)
	{
		if (CachedList != null)
			return CachedList;

		if (!File.Exists(FilePath))
			return CachedList = new();

		using FileStream Fs = new(FilePath, FileMode.Open, FileAccess.Read);
		using BinaryReader Br = new(Fs);

		int Version = Br.ReadInt32();
		if (Version != SerializationVersion)
		{
			throw new InvalidDataException($"Save file version {Version} is not supported by this build (expected {SerializationVersion}).");
		}

		int Count = Br.ReadInt32();
		CachedList = new(Count);

		for (int I = 0; I < Count; I++)
			CachedList.Add(ReadPlayback(Br, IsChild: false));

		return CachedList;
	}

	private static void WritePlayback(BinaryWriter Bw, OAudioPlayback Playback)
	{
		Playback.Writer?.Flush();

		byte[] Audio = Playback.IsRecording ? [] : Playback.MemoryStream.ToArray();
		Bw.Write(Audio.Length);

		if (Audio.Length > 0)
		{
			Bw.Write(Audio);
		}

		var SettingsJson = JsonSerializer.Serialize(Playback.PlaybackSettings, JsonOptions); 
		Bw.Write(SettingsJson);

		IReadOnlyList<(OAudioPlayback Playback, TimeSpan EmplaceTime)> Kids = Playback.GetChildren();
		Bw.Write(Kids.Count);

		foreach (var (Child, EmplaceTime) in Kids)
		{
			WritePlayback(Bw, Child);
			Bw.Write(EmplaceTime.Ticks);
		}
	}

	private static OAudioPlayback ReadPlayback(BinaryReader Br, bool IsChild)
	{
		OAudioPlayback Playback = new() { IsChild = IsChild };

		int AudioLength = Br.ReadInt32();
		if (AudioLength > 0)
		{
			byte[] Audio = Br.ReadBytes(AudioLength);
			Playback.MemoryStream.Write(Audio, 0, AudioLength);
			Playback.MemoryStream.Position = 0;
		}

		string SettingsJson = Br.ReadString();
		Playback.PlaybackSettings = JsonSerializer.Deserialize<FPlaybackSettings>(SettingsJson, JsonOptions) ?? new FPlaybackSettings();

		int ChildCount = Br.ReadInt32();
		for (int ChildIndex = 0; ChildIndex < ChildCount; ++ChildIndex)
		{
			OAudioPlayback Child = ReadPlayback(Br, IsChild: true);
			TimeSpan EmplaceTime = new(Br.ReadInt64());
			Playback.Children.Add((Child, EmplaceTime));
		}

		return Playback;
	}
}
