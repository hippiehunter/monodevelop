// 
// MainToolbar.cs
//  
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
// 
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using Gtk;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Core;
using System.Linq;
using MonoDevelop.Core.Assemblies;
using MonoDevelop.Components;
using Cairo;
using MonoDevelop.Ide.NavigateToDialog;
using MonoDevelop.Projects;
using System.Collections.Generic;
using Mono.Addins;
using MonoDevelop.Components.Commands.ExtensionNodes;
using MonoDevelop.Ide.Gui;


namespace MonoDevelop.Components.MainToolbar
{
	class MainToolbar: Gtk.EventBox, ICommandBar
	{
		const string ToolbarExtensionPath = "/MonoDevelop/Ide/CommandBar";

		HBox contentBox = new HBox (false, 0);

		ComboBox configurationCombo;
		TreeStore configurationStore = new TreeStore (typeof(string), typeof(string));

		ComboBox runtimeCombo;
		TreeStore runtimeStore = new TreeStore (typeof(string), typeof(string));

		StatusArea statusArea;

		SearchEntry matchEntry;

		ButtonBar buttonBar = new ButtonBar ();
		RoundButton button = new RoundButton ();
		Alignment buttonBarBox;

		HashSet<string> visibleBars = new HashSet<string> ();

		public Cairo.ImageSurface Background {
			get;
			set;
		}

		public int TitleBarHeight {
			get;
			set;
		}

		bool SearchForMembers {
			get {
				return PropertyService.Get ("MainToolbar.Search.IncludeMembers", true);
			}
			set {
				PropertyService.Set ("MainToolbar.Search.IncludeMembers", value);
			}
		}

		public MonoDevelop.Ide.StatusBar StatusBar {
			get {
				return statusArea;
			}
		}

		/*
		internal class SelectActiveRuntimeHandler : CommandHandler
		{
			protected override void Update (CommandArrayInfo info)
			{
			}
	
			protected override void Run (object dataItem)
			{
			}
		}
		*/
		public MainToolbar ()
		{
			IdeApp.Workspace.ActiveConfigurationChanged += (sender, e) => UpdateCombos ();
			IdeApp.Workspace.ActiveRuntimeChanged += (sender, e) => UpdateCombos ();
			IdeApp.Workspace.ConfigurationsChanged += (sender, e) => UpdateCombos ();

			IdeApp.Workspace.SolutionLoaded += (sender, e) => UpdateCombos ();
			IdeApp.Workspace.SolutionUnloaded += (sender, e) => UpdateCombos ();
			WidgetFlags |= Gtk.WidgetFlags.AppPaintable;

			AddWidget (button);
			AddSpace (8);

			configurationCombo = new Gtk.ComboBox ();
			configurationCombo.Model = configurationStore;
			var ctx = new Gtk.CellRendererText ();
			configurationCombo.PackStart (ctx, true);
			configurationCombo.AddAttribute (ctx, "text", 0);

			var configurationComboVBox = new VBox ();
			configurationComboVBox.PackStart (configurationCombo, true, false, 0);
			AddWidget (configurationComboVBox);
			AddSpace (8);

			runtimeCombo = new Gtk.ComboBox ();
			runtimeCombo.Model = runtimeStore;
			runtimeCombo.PackStart (ctx, true);
			runtimeCombo.AddAttribute (ctx, "text", 0);

			var runtimeComboVBox = new VBox ();
			runtimeComboVBox.PackStart (runtimeCombo, true, false, 0);
			AddWidget (runtimeComboVBox);

			buttonBarBox = new Alignment (0.5f, 0.5f, 0, 0);
			buttonBarBox.LeftPadding = 7;
			buttonBarBox.Add (buttonBar);
			buttonBarBox.NoShowAll = true;
			AddWidget (buttonBarBox);
			AddSpace (24);

			statusArea = new StatusArea ();

			var statusAreaVBox = new VBox ();
			statusAreaVBox.PackStart (statusArea, true, true, 0);
			contentBox.PackStart (statusAreaVBox, true, true, 0);
			statusArea.MaxWidth = 600;
			AddSpace (24);

			matchEntry = new SearchEntry ();

			CheckMenuItem includeMembers = this.matchEntry.AddFilterOption (2, GettextCatalog.GetString ("Include _Members"));
			includeMembers.DrawAsRadio = false;
			includeMembers.Active = SearchForMembers;
			includeMembers.Toggled += delegate {
				SearchForMembers = !SearchForMembers;
				if (popup != null) {
					string entry = matchEntry.Entry.Text;
					popup.Destroy ();
					popup = null;
					matchEntry.Entry.Text = entry;
				}
			};

			matchEntry.ForceFilterButtonVisible = true;

			var info = IdeApp.CommandService.GetCommand (MonoDevelop.Ide.NavigateToDialog.Commands.NavigateTo);

			if (info != null && !string.IsNullOrEmpty (info.AccelKey)) {
				matchEntry.EmptyMessage = GettextCatalog.GetString ("Press {0} for search.", KeyBindingManager.BindingToDisplayLabel (info.AccelKey, false));
			} else {
				matchEntry.EmptyMessage = GettextCatalog.GetString ("Enter text to search.");
			}
			matchEntry.Ready = true;
			matchEntry.Visible = true;
			matchEntry.IsCheckMenu = true;
			matchEntry.Entry.ModifyBase (StateType.Normal, Style.White);
			matchEntry.WidthRequest = 240;
			matchEntry.RoundedShape = true;
			matchEntry.Entry.Changed += HandleSearchEntryChanged;
			matchEntry.SizeAllocated += (o, args) => PositionPopup ();
			matchEntry.Activated += (sender, e) => {
				if (popup != null)
					popup.OpenFile ();
			};
			matchEntry.Entry.KeyPressEvent += (o, args) => {
				if (args.Event.Key == Gdk.Key.Escape) {
					var doc = IdeApp.Workbench.ActiveDocument;
					if (doc != null) {
						if (popup != null)
							popup.Destroy ();
						doc.Select ();
					}
					return;
				}
				if (popup != null) {
					args.RetVal = popup.ProcessKey (args.Event.Key, args.Event.State);
				}
			};
			IdeApp.Workbench.RootWindow.WidgetEvent += delegate(object o, WidgetEventArgs args) {
				if (args.Event is Gdk.EventConfigure)
					PositionPopup ();
			};

			BuildToolbar ();
			IdeApp.CommandService.RegisterCommandBar (buttonBar);

			AddinManager.ExtensionChanged += delegate(object sender, ExtensionEventArgs args) {
				if (args.PathChanged (ToolbarExtensionPath))
					BuildToolbar ();
			};

			contentBox.PackStart (matchEntry, false, false, 0);

			var align = new Gtk.Alignment (0, 0, 1f, 1f);
			align.Show ();
			align.TopPadding = 5;
			align.LeftPadding = 9;
			align.RightPadding = 18;
			align.BottomPadding = 10;
			align.Add (contentBox);

			Add (align);
			UpdateCombos ();

			button.Clicked += HandleStartButtonClicked;
			IdeApp.CommandService.RegisterCommandBar (this);
			this.ShowAll ();
			this.statusArea.statusIconBox.HideAll ();
		}

		protected override void OnRealized ()
		{
			base.OnRealized ();
			UpdateSize (-1, 21);
		}

		void UpdateSize (int comboHeight, int height)
		{
			configurationCombo.SetSizeRequest (150, comboHeight);
			runtimeCombo.SetSizeRequest (150, comboHeight);
			statusArea.SetSizeRequest (32, 32);
			matchEntry.HeightRequest = height + 4;
			buttonBar.HeightRequest = height + 2;
		}

		void AddSpace (int w)
		{
			Label la = new Label ("");
			la.WidthRequest = w;
			la.Show ();
			contentBox.PackStart (la, false, false, 0);
		}

		void BuildToolbar ()
		{
			buttonBar.Clear ();
			var bars = AddinManager.GetExtensionNodes (ToolbarExtensionPath).Cast<ItemSetCodon> ().Where (n => visibleBars.Contains (n.Id));
			if (!bars.Any ()) {
				buttonBarBox.Hide ();
				return;
			}

			buttonBarBox.Show ();
			buttonBar.ShowAll ();
			foreach (var bar in bars) {
				foreach (CommandItemCodon node in bar.ChildNodes.OfType<CommandItemCodon> ())
					buttonBar.Add (node.Id);
				buttonBar.AddSeparator ();
			}
		}

		public void ShowCommandBar (string barId)
		{
			visibleBars.Add (barId);
			BuildToolbar ();
		}

		public void HideCommandBar (string barId)
		{
			visibleBars.Remove (barId);
			BuildToolbar ();
		}

		SearchPopupWindow popup = null;

		public SearchPopupWindow SearchPopupWindow {
			get {
				return popup;
			}
		}
		public event EventHandler SearchPopupWindowCreated;

		protected virtual void OnSearchPopupWindowCreated (EventArgs e)
		{
			EventHandler handler = this.SearchPopupWindowCreated;
			if (handler != null)
				handler (this, e);
		}

		void HandleSearchEntryChanged (object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty (matchEntry.Entry.Text)){
				if (popup != null)
					popup.Destroy ();
				return;
			}
			if (popup == null) {
				popup = new SearchPopupWindow ();
				popup.SearchForMembers = SearchForMembers;
				popup.Destroyed += delegate {
					popup = null;
					matchEntry.Entry.Text = "";
				};
				popup.SizeAllocated += delegate {
					PositionPopup ();
				};
				PositionPopup ();
				popup.ShowAll ();
				OnSearchPopupWindowCreated (EventArgs.Empty);
			}
			popup.Update (matchEntry.Entry.Text);

		}

		void PositionPopup ()
		{
			if (popup == null)
				return;
			popup.ShowPopup (matchEntry, PopupPosition.TopRight);
		}

		void HandleRuntimeChanged (object sender, EventArgs e)
		{
			TreeIter iter;
			if (!runtimeStore.GetIterFromString (out iter, runtimeCombo.Active.ToString ()))
				return;
			var runtime = (string)runtimeStore.GetValue (iter, 1);
			string activeName;
			string platform;
			ItemConfiguration.ParseConfigurationId (IdeApp.Workspace.ActiveConfigurationId, out activeName, out platform);
			
			foreach (var conf in IdeApp.Workspace.GetConfigurations ()) {
				string name;
				ItemConfiguration.ParseConfigurationId (conf, out name, out platform);
				if (activeName == name && platform == runtime) {
					IdeApp.Workspace.ActiveConfigurationId = conf;
					SelectActiveConfiguration ();
					break;
				}
			}
		}

		void HandleConfigurationChanged (object sender, EventArgs e)
		{
			TreeIter iter;
			if (!configurationStore.GetIterFromString (out iter, configurationCombo.Active.ToString ()))
				return;
			var config = (string)configurationStore.GetValue (iter, 1);
			foreach (var conf in IdeApp.Workspace.GetConfigurations ()) {
				string name;
				string platform;
				ItemConfiguration.ParseConfigurationId (conf, out name, out platform);
				if (name == config) {
					IdeApp.Workspace.ActiveConfigurationId = conf;
					break;
				}
			}
		}

		void SelectActiveConfiguration ()
		{
			UpdateRuntimes ();
			configurationCombo.Changed -= HandleConfigurationChanged;
			runtimeCombo.Changed -= HandleRuntimeChanged;
			string name;
			string platform;
			ItemConfiguration.ParseConfigurationId (IdeApp.Workspace.ActiveConfigurationId, out name, out platform);
			int i = 0;
			Gtk.TreeIter iter;
			if (configurationStore.GetIterFirst (out iter)) {
				do {
					string val = (string)configurationStore.GetValue (iter, 1);
					if (name == val) {
						configurationCombo.Active = i;
						break;
					}
					i++;
				}
				while (configurationStore.IterNext (ref iter));
			}

			i = 0;
			if (runtimeStore.GetIterFirst (out iter)) {
				do {
					string val = (string)runtimeStore.GetValue (iter, 1);
					if (platform == val || platform == null && string.IsNullOrEmpty (val)) {
						runtimeCombo.Active = i;
						break;
					}
					i++;
				}
				while (runtimeStore.IterNext (ref iter));
			}
			runtimeCombo.Changed += HandleRuntimeChanged;
			configurationCombo.Changed += HandleConfigurationChanged;
		}

		void UpdateCombos ()
		{
			configurationCombo.Changed -= HandleConfigurationChanged;
			configurationStore.Clear ();
			if (!IdeApp.Workspace.IsOpen)
				return;
			var values = new HashSet<string> ();
			foreach (var conf in IdeApp.Workspace.GetConfigurations ()) {
				string name;
				string platform;
				ItemConfiguration.ParseConfigurationId (conf, out name, out platform);
				if (values.Contains (name))
					continue;
				values.Add (name);
				configurationStore.AppendValues (name, name);
			}
			configurationCombo.Changed += HandleConfigurationChanged;

			SelectActiveConfiguration ();
		}

		void UpdateRuntimes ()
		{
			runtimeCombo.Changed -= HandleRuntimeChanged;
			runtimeStore.Clear ();
			if (!IdeApp.Workspace.IsOpen)
				return;

			string name;
			string platform;
			ItemConfiguration.ParseConfigurationId (IdeApp.Workspace.ActiveConfigurationId, out name, out platform);
			var values = new HashSet<string> ();
			foreach (var conf in IdeApp.Workspace.GetConfigurations ()) {
				string curName;
				ItemConfiguration.ParseConfigurationId (conf, out curName, out platform);
				if (curName != name)
					continue;
				if (platform == null)
					platform = "";
				if (values.Contains (platform))
					continue;
				values.Add (platform);
				runtimeStore.AppendValues (string.IsNullOrEmpty (platform) ? "Any CPU" : platform, platform);
			}

			runtimeCombo.Changed += HandleRuntimeChanged;
		}

		public void AddWidget (Gtk.Widget widget)
		{
			contentBox.PackStart (widget, false, false, 0);
		}

		protected override bool OnExposeEvent (Gdk.EventExpose evnt)
		{
			using (var context = Gdk.CairoHelper.Create (evnt.Window)) {
				context.Rectangle (
					evnt.Area.X,
					evnt.Area.Y,
					evnt.Area.Width,
					evnt.Area.Height
				);
				context.Clip ();
				context.LineWidth = 1;
				if (Background != null && Background.Width > 0) {
					for (int x=0; x < Allocation.Width; x += Background.Width) {
						Background.Show (context, x, -TitleBarHeight);
					}
				} else {
					context.Rectangle (0, 0, Allocation.Width, Allocation.Height);
					using (var lg = new LinearGradient (0, 0, 0, Allocation.Height)) {
						lg.AddColorStop (0, (HslColor)Style.Light (StateType.Normal));
						lg.AddColorStop (1, (HslColor)Style.Mid (StateType.Normal));
						context.Pattern = lg;
					}
					context.Fill ();

				}
				context.MoveTo (0, Allocation.Height - 0.5);
				context.RelLineTo (Allocation.Width, 0);
				context.Color = Styles.ToolbarBottomBorderColor;
				context.Stroke ();

				context.MoveTo (0, Allocation.Height - 1.5);
				context.RelLineTo (Allocation.Width, 0);
				context.Color = Styles.ToolbarBottomGlowColor;
				context.Stroke ();

			}
			return base.OnExposeEvent (evnt);
		}

		[CommandHandler(MonoDevelop.Ide.NavigateToDialog.Commands.NavigateTo)]
		public void NavigateToCommand ()
		{
			matchEntry.Entry.GrabFocus ();
		}

		CommandInfo GetStartButtonCommandInfo ()
		{
			if (!IdeApp.ProjectOperations.CurrentRunOperation.IsCompleted)
				return IdeApp.CommandService.GetCommandInfo (MonoDevelop.Ide.Commands.ProjectCommands.Stop);
			else {
				var ci = IdeApp.CommandService.GetCommandInfo ("MonoDevelop.Debugger.DebugCommands.Debug");
				if (ci.Enabled)
					return ci;
				else
					// If debug is not enabled, try Run
					return IdeApp.CommandService.GetCommandInfo (MonoDevelop.Ide.Commands.ProjectCommands.Run);
			}
		}
		
		void HandleStartButtonClicked (object sender, EventArgs e)
		{
			var ci = GetStartButtonCommandInfo ();
			if (ci.Enabled)
				IdeApp.CommandService.DispatchCommand (ci.Command.Id);
		}
		
		#region ICommandBar implementation
		bool toolbarEnabled = true;

		void ICommandBar.Update (object activeTarget)
		{
			if (!toolbarEnabled)
				return;
			var ci = GetStartButtonCommandInfo ();
			if (ci.Enabled != button.Sensitive)
				button.Sensitive = ci.Enabled;
			button.ShowStart = IdeApp.ProjectOperations.CurrentRunOperation.IsCompleted;
		}

		void ICommandBar.SetEnabled (bool enabled)
		{
			toolbarEnabled = enabled;
			button.Sensitive = enabled;
			matchEntry.Sensitive = enabled;
		}
		#endregion
	}
}

