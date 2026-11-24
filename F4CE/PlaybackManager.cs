using F4CE.Objects;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

	public static readonly string SaveFolder = Path.Combine(AppContext.BaseDirectory, "playbacks");
	public const string SaveFileExtension = ".f4ce";

	private static readonly string LegacySavePath = Path.Combine(AppContext.BaseDirectory, "playbacks.bin");

	public static void RefreshPlaybacks()
	{
		CreateBasePlaybacks();
		ConvertLegacySaveFile();
		LoadSavedPlaybacks();
	}

	public static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
	private const int LegacySerializationVersion = 1;

	private static ReadOnlySpan<byte> SaveFileMagic => "F4CE"u8;
	private const int SaveFileVersion = 1;

	public static void SaveCopyOf(OAudioPlayback Playback)
	{
		OAudioPlayback Saved = Playback.Duplicate();
		Saved.IsChild = false;

		try
		{
			Directory.CreateDirectory(SaveFolder);
			string SavedAt = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
			string FilePath = MakeUniquePath(SaveFolder, SavedAt, SaveFileExtension);

			WritePlaybackFile(Saved, FilePath);
			Saved.SaveFile = FilePath;
			StoredPlaybacks.Add(Saved);
			Console.WriteLine($"Saved {Path.GetFileName(FilePath)}");
		}
		catch (Exception Ex)
		{
			Saved.Dispose();
			Console.WriteLine($"Couldn't save the playback: {Ex.Message}");
		}
	}

	public static void DeleteSavedPlayback(OAudioPlayback Playback)
	{
		if (Playback.SaveFile != null)
		{
			try
			{
				File.Delete(Playback.SaveFile);
			}
			catch (Exception Ex)
			{
				Console.WriteLine($"Couldn't delete {Path.GetFileName(Playback.SaveFile)}: {Ex.Message}");
				return;
			}
		}

		Playback.StopPlayback();
		StoredPlaybacks.Remove(Playback);
	}

	public static void LoadSavedPlaybacks()
	{
		foreach (OAudioPlayback Old in StoredPlaybacks)
		{
			Old.StopPlayback();
		}

		StoredPlaybacks.Clear();

		if (!Directory.Exists(SaveFolder))
		{
			return;
		}

		List<string> Files = Directory.EnumerateFiles(SaveFolder)
			.Where(FilePath => Path.GetExtension(FilePath).Equals(SaveFileExtension, StringComparison.OrdinalIgnoreCase))
			.OrderBy(FilePath => Path.GetFileName(FilePath), StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (string FilePath in Files)
		{
			try
			{
				StoredPlaybacks.Add(ReadPlaybackFile(FilePath));
			}
			catch (Exception Ex)
			{
				Console.WriteLine($"Skipped {Path.GetFileName(FilePath)}: {Ex.Message}");
			}
		}

		Console.WriteLine($"Loaded {StoredPlaybacks.Count} saved playback(s) from {SaveFolder}");
	}

	public static void OpenSaveFolder()
	{
		try
		{
			Directory.CreateDirectory(SaveFolder);
			Process.Start(new ProcessStartInfo { FileName = SaveFolder, UseShellExecute = true });
		}
		catch (Exception Ex)
		{
			Console.WriteLine($"Couldn't open {SaveFolder}: {Ex.Message}");
		}
	}

	public static void ConvertLegacySaveFile()
	{
		if (!File.Exists(LegacySavePath))
		{
			return;
		}

		string LegacyName = Path.GetFileName(LegacySavePath);
		Console.WriteLine($"Converting {LegacyName} into separate playback files...");

		string NamePrefix = File.GetLastWriteTime(LegacySavePath).ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_legacy-";
		int Converted = 0;
		bool AllConverted = true;

		try
		{
			Directory.CreateDirectory(SaveFolder);

			using FileStream Fs = new(LegacySavePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using BinaryReader Br = new(Fs);

			int Version = Br.ReadInt32();
			if (Version != LegacySerializationVersion)
			{
				throw new InvalidDataException($"it's version {Version}, expected {LegacySerializationVersion}");
			}

			int Count = Br.ReadInt32();
			if (Count < 0)
			{
				throw new InvalidDataException($"it says it holds {Count} playbacks");
			}

			int Digits = Math.Max(2, Count.ToString(CultureInfo.InvariantCulture).Length);

			for (int Index = 0; Index < Count; ++Index)
			{
				using OAudioPlayback Playback = new()
				{
					IsChild = false,
				};
				Playback.ReadPlayback(Br);

				string Number = (Index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
				string FilePath = Path.Combine(SaveFolder, NamePrefix + Number + SaveFileExtension);

				if (File.Exists(FilePath))
				{
					continue;
				}

				try
				{
					WritePlaybackFile(Playback, FilePath);
					++Converted;
				}
				catch (Exception Ex)
				{
					AllConverted = false;
					Console.WriteLine($"Couldn't write {Path.GetFileName(FilePath)}: {Ex.Message}");
				}
			}
		}
		catch (Exception Ex)
		{
			AllConverted = false;
			Console.WriteLine($"Couldn't convert all of {LegacyName}: {Ex.Message}");
		}

		if (!AllConverted)
		{
			Console.WriteLine($"Left {LegacyName} where it is, converting it will be tried again next time F4CE starts");
			return;
		}

		try
		{
			string BackupPath = MakeUniquePath(Path.GetDirectoryName(LegacySavePath), LegacyName, ".old");
			File.Move(LegacySavePath, BackupPath);
			Console.WriteLine($"Converted {Converted} playback(s), the old file is now {Path.GetFileName(BackupPath)}");
		}
		catch (Exception Ex)
		{
			Console.WriteLine($"Converted {Converted} playback(s) but couldn't rename {LegacyName}: {Ex.Message}");
		}
	}

	private static void WritePlaybackFile(OAudioPlayback Playback, string FilePath)
	{
		string TempPath = FilePath + ".tmp";

		try
		{
			using (FileStream Fs = new(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
			using (BinaryWriter Bw = new(Fs))
			{
				Bw.Write(SaveFileMagic);
				Bw.Write(SaveFileVersion);
				Playback.WritePlayback(Bw);
				Bw.Flush();
				Fs.Flush(flushToDisk: true);
			}

			File.Move(TempPath, FilePath);
		}
		catch
		{
			try
			{
				File.Delete(TempPath);
			}
			catch
			{
				// the error that matters is the one being rethrown
			}

			throw;
		}
	}

	private static OAudioPlayback ReadPlaybackFile(string FilePath)
	{
		using FileStream Fs = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using BinaryReader Br = new(Fs);

		if (!Br.ReadBytes(SaveFileMagic.Length).AsSpan().SequenceEqual(SaveFileMagic))
		{
			throw new InvalidDataException("it isn't an F4CE playback file");
		}

		int Version = Br.ReadInt32();
		if (Version != SaveFileVersion)
		{
			throw new InvalidDataException($"it's file format version {Version}, this F4CE reads version {SaveFileVersion}");
		}

		OAudioPlayback Playback = new()
		{
			IsChild = false,
		};

		try
		{
			Playback.ReadPlayback(Br);
		}
		catch
		{
			Playback.Dispose();
			throw;
		}

		Playback.SaveFile = FilePath;
		return Playback;
	}

	private static string MakeUniquePath(string Folder, string Name, string Extension)
	{
		string FilePath = Path.Combine(Folder, Name + Extension);

		for (int Number = 2; File.Exists(FilePath); ++Number)
		{
			FilePath = Path.Combine(Folder, $"{Name}_{Number}{Extension}");
		}

		return FilePath;
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

			// a kid with its window open is already in its parent's mix, don't export it twice
			if (ActivePlaybacks.Any(Other => Other != Playback && Other.HasKid(Playback)))
			{
				continue;
			}

			Playback.ExportProvider(out var Export);
			RenderedProviders.AddRange(Export);
		}

		ExportProviders(RenderedProviders);
	}
}
