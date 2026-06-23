using F4CE.Backends;
using F4CE.Objects;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace F4CE;

internal class Window : GameWindow
{
	public Window() : base(GameWindowSettings.Default, new NativeWindowSettings() { ClientSize = new OpenTK.Mathematics.Vector2i(1280, 960), APIVersion = new Version(3, 3) })
	{ }


	protected override void OnLoad()
	{
		base.OnLoad();

		Title = $"F4CE ({GL.GetString(StringName.Version)})";

		GL.DebugMessageCallback(DebugProcCallback, IntPtr.Zero);
		GL.Enable(EnableCap.DebugOutput);
		GL.Enable(EnableCap.DebugOutputSynchronous);

		ImGui.CreateContext();
		ImGuiIOPtr IO = ImGui.GetIO();
		IO.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
		IO.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
		IO.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
		IO.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

		ImGui.StyleColorsDark();

		ImGuiStylePtr Style = ImGui.GetStyle();
		if ((IO.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
		{
			Style.WindowRounding = 0.0f;
			Style.Colors[(int)ImGuiCol.WindowBg].W = 1.0f;
		}

		ImguiImplOpenTK4.Init(this);
		ImguiImplOpenGL3.Init();

		PlaybackManager.RefreshPlaybacks(); // !
	}

	protected override void OnRenderFrame(FrameEventArgs EventArgs)
	{
		base.OnRenderFrame(EventArgs);

		ImguiImplOpenGL3.NewFrame();
		ImguiImplOpenTK4.NewFrame();
		ImGui.NewFrame();

		ImGui.DockSpaceOverViewport();
		//ImGui.ShowDemoWindow();

		DrawMainImgui();

		ImGui.Render();
		GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
		GL.ClearColor(new OpenTK.Mathematics.Color4(0, 32, 48, 255));
		GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
		ImguiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

		if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
		{
			ImGui.UpdatePlatformWindows();
			ImGui.RenderPlatformWindowsDefault();
			Context.MakeCurrent();
		}

		SwapBuffers();
	}

	public static void OnClosed()
	{
		Console.WriteLine($"Saving!");

		PlaybackManager.SaveListToFile();

		ImguiImplOpenGL3.Shutdown();
		ImguiImplOpenTK4.Shutdown();
	}

	private static void DrawPlaybacks()
	{
		for (int PlaybackIndex = PlaybackManager.StoredPlaybacks.Count - 1; PlaybackIndex >= 0; --PlaybackIndex)
		{
			var LoadedPlayback = PlaybackManager.StoredPlaybacks[PlaybackIndex];
			ImGui.PushID($"{LoadedPlayback.ImGuiD}");
			if (ImGui.Button($"{LoadedPlayback.ImGuiD} {LoadedPlayback.GetTotalDuration()}"))
			{
				if (LoadedPlayback.IsPlaying)
				{
					LoadedPlayback.StopPlayback();
				}
				else
				{
					LoadedPlayback.StartPlayback();
				}
			}
			ImGui.SameLine();
			if (ImGui.Button("open"))
			{
				OAudioPlayback ChildClone = new(LoadedPlayback)
				{
					IsChild = true,
				};
				PlaybackManager.ActivePlaybacks.Add(ChildClone);
			}
			ImGui.SameLine();
			if (ImGui.Button("delete"))
			{
				PlaybackManager.StoredPlaybacks.RemoveAt(PlaybackIndex);
			}
			ImGui.PopID();
		}
	}
		
	

	private static void DrawMainImgui()
	{
		ImGui.Begin("main");

		for (int PlaybackIndex = 0; PlaybackIndex < PlaybackManager.ActivePlaybacks.Count; ++PlaybackIndex)
		{
			var StoredPlayback = PlaybackManager.ActivePlaybacks[PlaybackIndex];
			StoredPlayback.DrawBlock();
			ImGui.NewLine();
			ImGui.NewLine();
			ImGui.NewLine();
		}
		ImGui.NewLine();
		if (ImGui.Button("FOLLOW THAT CHUNE", new Vector2(260, 30)))
		{
			PlaybackManager.SaveAllPlaybacksToFile();
		}
		ImGui.NewLine();
		DrawPlaybacks();

		ImGui.End();
	}


	public readonly static DebugProc DebugProcCallback = Window_DebugProc;
	private static void Window_DebugProc(DebugSource Source, DebugType Type, int Id, DebugSeverity Severity, int Length, IntPtr PtrMessage, IntPtr PtrInt)
	{
		if (Source == DebugSource.DebugSourceApi)
		{
			return;
		}

		var ParsedMessage = Marshal.PtrToStringAnsi(PtrMessage, Length);
		Console.WriteLine($"[{Source}] {ParsedMessage}");
	}
}
