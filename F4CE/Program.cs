using F4CE.Backends;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace F4CE;

class Program
{
	static void Main()
	{
		Window Window = new();
		Window.Run();
		Window.OnClosed();
	}
}
