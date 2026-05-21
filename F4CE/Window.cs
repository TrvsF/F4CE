using F4CE.Backends;
using F4CE.Objects;
using ImGuiNET;
using NAudio.Wave;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace F4CE;

class Window : GameWindow
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
	}

	protected override void OnRenderFrame(FrameEventArgs EventArgs)
	{
		base.OnRenderFrame(EventArgs);

		ImguiImplOpenGL3.NewFrame();
		ImguiImplOpenTK4.NewFrame();
		ImGui.NewFrame();

		ImGui.DockSpaceOverViewport();
		ImGui.ShowDemoWindow();

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

	private readonly OAudioPlayback GenericPlayback = new();

	private void DrawMainImgui()
	{
		ImGui.Begin("main");

		if (!GenericPlayback.IsRecording)
		{
			if (ImGui.Button("Start Recording", new Vector2(160, 40)))
			{
				GenericPlayback.StartRecording();
			}
		}
		else
		{
			if (ImGui.Button("Stop Recording", new Vector2(160, 40)))
			{
				GenericPlayback.StopRecording();
			}
		}

		ImGui.SameLine();

		bool PlaybackDisabled = GenericPlayback.IsRecording;

		if (PlaybackDisabled)
		{
			ImGui.BeginDisabled();
		}

		if (!GenericPlayback.IsPlaying)
		{
			if (ImGui.Button("Play Recording", new Vector2(160, 40)))
			{
				GenericPlayback.PlayRecording();
			}
		}
		else
		{
			if (ImGui.Button("Stop Playback", new Vector2(160, 40)))
			{
				GenericPlayback.StopPlayback();
			}
		}

		if (PlaybackDisabled)
		{
			ImGui.EndDisabled();
		}

		ImGui.End();
	}

	public void OnClosed()
	{
		ImguiImplOpenGL3.Shutdown();
		ImguiImplOpenTK4.Shutdown();
	}

	public readonly static DebugProc DebugProcCallback = Window_DebugProc;
	private static void Window_DebugProc(DebugSource Source, DebugType Type, int Id, DebugSeverity Severity, int Length, IntPtr PtrMessage, IntPtr PtrInt)
	{
		string ParsedMessage = Marshal.PtrToStringAnsi(PtrMessage, Length);
		bool ShowMessage = true;

		switch (Source)
		{
			case DebugSource.DebugSourceApplication:
				ShowMessage = false;
				break;
			case DebugSource.DontCare:
			case DebugSource.DebugSourceApi:
			case DebugSource.DebugSourceWindowSystem:
			case DebugSource.DebugSourceShaderCompiler:
			case DebugSource.DebugSourceThirdParty:
			case DebugSource.DebugSourceOther:
			default:
				ShowMessage = true;
				break;
		}

		if (ShowMessage)
		{
			switch (Severity)
			{
				case DebugSeverity.DontCare:
					Console.WriteLine($"[DontCare] [{Source}] {ParsedMessage}");
					break;
				case DebugSeverity.DebugSeverityHigh:
					Console.Error.WriteLine($"Error: [{Source}] {ParsedMessage}");
					break;
				case DebugSeverity.DebugSeverityMedium:
					Console.WriteLine($"Warning: [{Source}] {ParsedMessage}");
					break;
				case DebugSeverity.DebugSeverityLow:
					Console.WriteLine($"Info: [{Source}] {ParsedMessage}");
					break;
				case DebugSeverity.DebugSeverityNotification:
					//Console.WriteLine($"[Fuck THis] [{Source}] {ParsedMessage}");
					break;
				default:
					Console.WriteLine($"[{Severity}] [{Source}] {ParsedMessage}");
					break;
			}
		}
	}
}
