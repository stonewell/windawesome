﻿using System;
using System.Collections.Generic;

namespace Windawesome
{
	public class WindowBase
	{
		public readonly IntPtr hWnd;
		public readonly IntPtr rootOwner;

		public WindowBase(IntPtr hWnd)
		{
			this.hWnd = hWnd;
			this.rootOwner = Utilities.GetRootOwner(hWnd);
		}

		internal WindowBase(WindowBase window)
		{
			this.hWnd = window.hWnd;
			this.rootOwner = window.rootOwner;
		}
	}

	public class Window : WindowBase
	{
		public bool IsFloating { get; internal set; }
		public State Titlebar { get; internal set; }
		public State InAltTabAndTaskbar { get; internal set; }
		public State WindowBorders { get; internal set; }
		public int WorkspacesCount { get; internal set; } // if > 1 window is shared between two or more workspaces
		public string DisplayName { get; internal set; }
		public readonly string className;
		public readonly string processName;
		public readonly bool is64BitProcess;
		public readonly bool redrawOnShow;
		public bool ShowMenu { get; private set; }
		public readonly bool updateIcon;
		public readonly OnWindowCreatedOrShownAction onHiddenWindowShownAction;
		public readonly IntPtr menu;
		public readonly bool hideFromAltTabAndTaskbarWhenOnInactiveWorkspace;

		private readonly NativeMethods.WS originalStyle;
		private readonly NativeMethods.WS_EX originalExStyle;

		private NativeMethods.WINDOWPLACEMENT windowPlacement;
		private readonly NativeMethods.WINDOWPLACEMENT originalWindowPlacement;

		private readonly ProgramRule.CustomMatchingFunction customOwnedWindowMatchingFunction;

		private readonly LinkedList<IntPtr> ownedWindows;

		internal IEnumerable<IntPtr> GetOwnedWindows()
		{
			if (ownedWindows.Count == 1)
			{
				return ownedWindows;
			}

			// handling of owned windows is done like that because Windows
			// is inconsistent in sending messages about owned window hiding/destruction
			// as of Windows 7 SP1
			return NativeMethods.IsWindowVisible(hWnd) ?
				GetAndRemoveWindows(NativeMethods.IsWindowVisible) :
				GetAndRemoveWindows(NativeMethods.IsWindow);
		}

		private IEnumerable<IntPtr> GetAndRemoveWindows(Predicate<IntPtr> predicate)
		{
			var windowNode = ownedWindows.First;
			yield return windowNode.Value;
			for (windowNode = windowNode.Next; windowNode != null; windowNode = windowNode.Next)
			{
				if (!predicate(windowNode.Value))
				{
					var oldWindowNode = windowNode;
					windowNode = windowNode.Previous;
					ownedWindows.Remove(oldWindowNode);
				}
				else
				{
					yield return windowNode.Value;
				}
			}
		}

		internal bool AddToOwnedWindows(IntPtr hWnd)
		{
			if (customOwnedWindowMatchingFunction(hWnd))
			{
				// checking the whole list is necessary as otherwise hWnd could be added multiple times
				// this happens, e.g., with Windows Media Player 12 - when it is shown, all its owned windows
				// (which are dozens), are added again if it weren't for this check
				if (ownedWindows.FindLast(hWnd) == null)
				{
					ownedWindows.AddLast(hWnd);
				}
				return true;
			}
			return false;
		}

		internal Window(IntPtr hWnd, string className, string displayName, string processName, int workspacesCount, bool is64BitProcess,
			NativeMethods.WS originalStyle, NativeMethods.WS_EX originalExStyle, ProgramRule.Rule rule, ProgramRule programRule, IntPtr menu) :
			base(hWnd)
		{
			IsFloating = rule.isFloating;
			Titlebar = rule.titlebar;
			InAltTabAndTaskbar = rule.inAltTabAndTaskbar;
			WindowBorders = rule.windowBorders;
			this.WorkspacesCount = workspacesCount;
			this.DisplayName = displayName;
			this.className = className;
			this.processName = processName;
			this.is64BitProcess = is64BitProcess;
			redrawOnShow = rule.redrawOnShow;
			ShowMenu = programRule.showMenu;
			updateIcon = programRule.updateIcon;
			onHiddenWindowShownAction = programRule.onHiddenWindowShownAction;
			this.menu = menu;
			this.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace = rule.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace;

			this.originalStyle = originalStyle;
			this.originalExStyle = originalExStyle;

			windowPlacement = NativeMethods.WINDOWPLACEMENT.Default;
			SavePosition();
			originalWindowPlacement = windowPlacement;

			this.customOwnedWindowMatchingFunction = programRule.customOwnedWindowMatchingFunction;

			this.ownedWindows = new LinkedList<IntPtr>();
			this.ownedWindows.AddFirst(hWnd);
		}

		internal Window(Window window) : base(window)
		{
			this.IsFloating = window.IsFloating;
			this.Titlebar = window.Titlebar;
			this.InAltTabAndTaskbar = window.InAltTabAndTaskbar;
			this.WindowBorders = window.WindowBorders;
			this.WorkspacesCount = window.WorkspacesCount;
			this.DisplayName = window.DisplayName;
			className = window.className;
			processName = window.processName;
			is64BitProcess = window.is64BitProcess;
			redrawOnShow = window.redrawOnShow;
			ShowMenu = window.ShowMenu;
			updateIcon = window.updateIcon;
			onHiddenWindowShownAction = window.onHiddenWindowShownAction;
			menu = window.menu;
			this.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace = window.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace;

			this.originalStyle = window.originalStyle;
			this.originalExStyle = window.originalExStyle;

			windowPlacement = window.windowPlacement;
			originalWindowPlacement = window.originalWindowPlacement;

			this.customOwnedWindowMatchingFunction = window.customOwnedWindowMatchingFunction;
			ownedWindows = window.ownedWindows;
		}

		public override int GetHashCode()
		{
			return hWnd.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			var window = obj as Window;
			return window != null && window.hWnd == hWnd;
		}

		internal void Initialize()
		{
			var style = NativeMethods.GetWindowStyleLongPtr(hWnd);
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			var prevStyle = style;
			var prevExStyle = exStyle;

			var titlebarStyle = originalStyle & NativeMethods.WS.WS_OVERLAPPEDWINDOW;

			var borderStyle = originalStyle & NativeMethods.WS.WS_SIZEBOX;

			var borderExStyle = originalExStyle &
				(NativeMethods.WS_EX.WS_EX_OVERLAPPEDWINDOW |
				NativeMethods.WS_EX.WS_EX_DLGMODALFRAME | NativeMethods.WS_EX.WS_EX_STATICEDGE);

			if (this.InAltTabAndTaskbar != State.AS_IS)
			{
				ShowInAltTabAndTaskbar(this.InAltTabAndTaskbar == State.SHOWN);
			}
			switch (this.Titlebar)
			{
				case State.SHOWN:
					style |= titlebarStyle;
					break;
				case State.HIDDEN:
					style &= ~titlebarStyle;
					break;
			}
			switch (this.WindowBorders)
			{
				case State.SHOWN:
					style |= borderStyle;
					exStyle |= borderExStyle;
					break;
				case State.HIDDEN:
					style &= ~borderStyle;
					exStyle &= ~borderExStyle;
					break;
			}

			if (style != prevStyle)
			{
				NativeMethods.SetWindowStyleLongPtr(hWnd, style);
			}
			if (exStyle != prevExStyle)
			{
				NativeMethods.SetWindowExStyleLongPtr(hWnd, exStyle);
			}

			if (style != prevStyle || exStyle != prevExStyle)
			{
				Redraw();
			}
		}

		internal void ToggleShowHideInTaskbar()
		{
			this.InAltTabAndTaskbar = (State) (((int) this.InAltTabAndTaskbar + 1) % 2);
			ShowInAltTabAndTaskbar(this.InAltTabAndTaskbar == State.SHOWN);
		}

		internal void ShowInAltTabAndTaskbar(bool show)
		{
			// show/hide from Alt-Tab menu
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			NativeMethods.SetWindowExStyleLongPtr(hWnd,
				show ?
				(exStyle | NativeMethods.WS_EX.WS_EX_APPWINDOW) & ~NativeMethods.WS_EX.WS_EX_TOOLWINDOW :
				(exStyle & ~NativeMethods.WS_EX.WS_EX_APPWINDOW) | NativeMethods.WS_EX.WS_EX_TOOLWINDOW);

			// show/hide from Taskbar
			NativeMethods.PostMessage(SystemAndProcessInformation.taskbarButtonsWindowHandle, NativeMethods.WM_SHELLHOOKMESSAGE,
				(UIntPtr) (uint) (show ? NativeMethods.ShellEvents.HSHELL_WINDOWCREATED : NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED), hWnd);
		}

		internal void ToggleShowHideTitlebar()
		{
			this.Titlebar = (State) (((int) this.Titlebar + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideWindowBorder()
		{
			this.WindowBorders = (State) (((int) this.WindowBorders + 1) % 2);
			Initialize();
		}

		internal bool ToggleShowHideWindowMenu()
		{
			ShowMenu = !ShowMenu;
			return ShowHideWindowMenu();
		}

		internal void Redraw()
		{
			// this whole thing is a hack but I've found no other way to make it work (and I've tried
			// a zillion things). Resizing seems to do the best job.
			NativeMethods.RECT rect;
			NativeMethods.GetWindowRect(hWnd, out rect);
			NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top - 1,
				NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOMOVE |
				NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOACTIVATE |
				NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_NOCOPYBITS);
			NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top,
				NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOMOVE |
				NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOACTIVATE |
				NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_NOCOPYBITS);

			NativeMethods.RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero,
				NativeMethods.RDW.RDW_ALLCHILDREN |
				NativeMethods.RDW.RDW_ERASE |
				NativeMethods.RDW.RDW_INVALIDATE);
		}

		internal void SavePosition()
		{
			NativeMethods.GetWindowPlacement(hWnd, ref windowPlacement);
		}

		internal void RestorePosition(bool doNotShow)
		{
			var oldShowCmd = windowPlacement.ShowCmd;
			if (doNotShow)
			{
				windowPlacement.ShowCmd = NativeMethods.SW.SW_HIDE;
			}
			else
			{
				switch (windowPlacement.ShowCmd)
				{
					case NativeMethods.SW.SW_SHOWNORMAL:
						windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNOACTIVATE;
						break;
					case NativeMethods.SW.SW_SHOW:
						windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNA;
						break;
					case NativeMethods.SW.SW_SHOWMINIMIZED:
						windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWMINNOACTIVE;
						break;
				}
			}
			windowPlacement.Flags |= NativeMethods.WPF.WPF_ASYNCWINDOWPLACEMENT;
			NativeMethods.SetWindowPlacement(hWnd, ref windowPlacement);

			if (doNotShow)
			{
				windowPlacement.ShowCmd = oldShowCmd;
			}
		}

		internal void ShowAsync()
		{
			if (this.redrawOnShow)
			{
				this.Redraw();
			}
			GetOwnedWindows().ForEach(h => NativeMethods.ShowWindowAsync(h, NativeMethods.SW.SW_SHOWNA));
		}

		internal bool ShowHideWindowMenu()
		{
			return menu != IntPtr.Zero && NativeMethods.SetMenu(this.hWnd, this.ShowMenu ? this.menu : IntPtr.Zero);
		}

		internal void RevertToInitialValues()
		{
			this.Titlebar = State.SHOWN;
			this.InAltTabAndTaskbar = State.SHOWN;
			this.WindowBorders = State.SHOWN;
			Initialize();

			if (!ShowMenu)
			{
				ToggleShowHideWindowMenu();
			}

			windowPlacement = originalWindowPlacement;
			RestorePosition(false);
			ShowAsync();
		}
	}
}
