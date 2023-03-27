using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot.Collections;

public partial class DebugSuite_Manager : CanvasLayer
{
    #region Events
    public delegate void EventToggle();
    public EventToggle eventOpen;
    public EventToggle eventClose;
    public EventToggle eventFreeze;
    public EventToggle eventUnfreeze;
	#endregion

	string configPath = "res://Debug Suite/Settings.cfg";
	string windowScenePath = "res://Debug Suite/Resources/Debug suite window.tscn";
	DebugSuite_Window window;

	Control content;

    Label pinnedTemplate;
    PopupMenu pinnedMenu;
    PopupMenu mainMenu;
	PopupPanel pinnedRename;
	LineEdit pinnedRenameInput;

    //Buttons
    Button buttonMenu, buttonPinnedMove, buttonPinnedSize, buttonPinnedMinimize, buttonPinnedAlignment;
	bool pinnedAdjust;

    Button closeButton, freezeButton;
	PopupMenu rebindMenu;
	ColorRect rebindRect;
	int keyToRebind;
	bool rebinding;

    //Interace
    BoxContainer pinnedList;
	List<Label> pinnedLabels = new();
	List<FieldInfo> pinnedFields = new();
	List<object> pinnedObjs = new();
	int pinnedSelected;

	List<DebugSuite_Window> inspectors = new();

	public Array<string> nodeBookmarks = new();

	public Vector2I MousePosition => new Vector2I((int)GetWindow().GetMousePosition().X, (int)GetWindow().GetMousePosition().Y);

    //Settings
    string settingsSec = "Settings";
	ConfigFile config;

    //Hotkeys
    public Key toggleKey = Key.Tab;
	public Key freezeKey = Key.Pageup;

	bool frozen;

	public override void _Ready()
	{
		config = new ConfigFile();
		config.Load(configPath);
		toggleKey = (Key)((long)config.GetValue(settingsSec, nameof(toggleKey), (long)Key.Tab));
		freezeKey = (Key)((long)config.GetValue(settingsSec, nameof(freezeKey), (long)Key.Pageup));

		content = GetNode<Control>("Content");
		pinnedList = GetNode<BoxContainer>("Pinned");

		pinnedTemplate = GetNode<Label>("Pinned template");
		pinnedTemplate.Visible = false;
		pinnedTemplate.AddThemeFontSizeOverride("font_size", (int)config.GetValue(settingsSec, "pinnedSize", 16));

		pinnedMenu = GetNode<PopupMenu>("Pinned menu");
		pinnedMenu.IdPressed += PinnedMenuSelect;

		pinnedRename = GetNode<PopupPanel>("Pinned rename");
		pinnedRenameInput = pinnedRename.GetNode<LineEdit>("Rename");
		pinnedRenameInput.TextSubmitted += PinnedRename;

        mainMenu = GetNode<PopupMenu>("Menu");
		mainMenu.IdPressed += MainMenuSelect;

		closeButton = content.GetNode<Button>("Close");
		closeButton.Pressed += SwitchContent;
		closeButton.GuiInput += (InputEvent e) => RebindInput(e, 0);
		closeButton.Text = $"[{toggleKey}]";
		freezeButton = content.GetNode<Button>("Freeze");
		freezeButton.Pressed += SwitchFreeze;
		freezeButton.GuiInput += (InputEvent e) => RebindInput(e, 1);
		freezeButton.Text = $"[{freezeKey}]";

		rebindMenu = GetNode<PopupMenu>("Rebind");
		rebindMenu.IdPressed += RebindPressed;
		rebindRect = GetNode<ColorRect>("Rebing popup");

		//Buttons
		buttonMenu = content.GetNode<Button>("Menu");
		buttonMenu.Pressed += ShowMainMenu;
		buttonPinnedMove = content.FindChild("Pinned move") as Button;
		buttonPinnedMove.GuiInput += (InputEvent e) => PinnedInput(e, 0);
		buttonPinnedSize = content.FindChild("Pinned size") as Button;
        buttonPinnedSize.GuiInput += (InputEvent e) => PinnedInput(e, 1);
		buttonPinnedMinimize = content.FindChild("Pinned minimize") as Button;
		buttonPinnedMinimize.Pressed += () => { pinnedList.Visible = !pinnedList.Visible; };
		buttonPinnedAlignment = content.FindChild("Pinned alignment") as Button;
        buttonPinnedAlignment.Pressed += () =>{foreach (var item in pinnedLabels)
			{ item.HorizontalAlignment = item.HorizontalAlignment == HorizontalAlignment.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left; } };

		Initialize();
	}

	public override void _Process(double delta)
	{
		UpdatePinnedVariables();
	}

	void Initialize()
	{
		LoadLayout();
		LoadPinned();
		LoadNodeBookmarks();

		content.Visible = false;
	}

	void AddInspector(Vector2? position = null, Vector2? size = null, float scale = 1, string path = "", bool minimized = false)
	{
		window = GD.Load<PackedScene>(windowScenePath).Instantiate() as DebugSuite_Window;
		content.AddChild(window);
		inspectors.Add(window);
		if(path != "") window.GetNode<DebugSuite_Inspector>("Inspector panel").SelectNode(path);

		if (position != null) window.Position = (Vector2)position;
		if (size != null) window.Size = (Vector2)size;
		window.Scale = new Vector2(scale, scale);
		if (minimized) window.SwitchWindow(0);
	}

	public override void _Input(InputEvent @event)
	{
		if(@event is InputEventKey key)
		{
			if(key.Keycode == toggleKey && key.Pressed)
			{
				SwitchContent();
			}
			if(key.Keycode == freezeKey && key.Pressed)
			{
				SwitchFreeze();
			}
			if (rebinding)
			{
				if (key.Pressed)
				{
					switch (keyToRebind)
					{
						case 0:
							toggleKey = key.Keycode;
							break;
						case 1:
							freezeKey = key.Keycode;
							break;
					}
					config.SetValue(settingsSec, nameof(toggleKey), (long)toggleKey);
                    closeButton.Text = $"[{toggleKey}]";
                    config.SetValue(settingsSec, nameof(freezeKey), (long)freezeKey);
                    freezeButton.Text = $"[{freezeKey}]";

					config.Save(configPath);

                    rebindRect.Visible = false;
					rebinding = false;
				}
			}
		}
	}

	void SwitchContent()
	{
		content.Visible = !content.Visible;
				
		if (content.Visible) eventOpen?.Invoke();
		else eventClose?.Invoke();
	}

	void SwitchFreeze()
	{
		frozen = !frozen;
		for (int i = 0; i < inspectors.Count; i++)
		{
			inspectors[i].Freeze(frozen);
		}
		foreach (var item in content.GetChildren())
		{
			if(item is not DebugSuite_Window)
				((Control)item).Visible = !frozen;
		}
		if (frozen) eventFreeze?.Invoke();
		else eventUnfreeze?.Invoke();
	}

    #region Pin
    public void PinVariable(string path, string name, FieldInfo field, object obj)
	{
		var label = pinnedTemplate.Duplicate() as Label;
		pinnedList.AddChild(label);
		label.Visible = true;
		label.SetMeta("index", pinnedLabels.Count);
		label.SetMeta("name", name);
		label.SetMeta("path", path);
		label.GuiInput += (InputEvent e) => PinnedInput(e, label);

		pinnedLabels.Add(label);
		pinnedFields.Add(field);
		pinnedObjs.Add(obj);
	}

    public void PinVariableFromScript(string id, Variant value)
	{
		if (id == "") return;
		for (int i = 0; i < pinnedLabels.Count; i++)
		{
			if (pinnedLabels[i].Name == "c$" + id) 
			{
				pinnedLabels[i].Text = $"{id}: {value}";
				return; 
			}
		}

		var label = pinnedTemplate.Duplicate() as Label;
		pinnedList.AddChild(label);
		label.Visible = true;
		label.Text = $"{id}: {value}";
		label.Name = "c$" + id;
		label.SetMeta("index", pinnedLabels.Count);
		label.SetMeta("fromScript", true);

		pinnedLabels.Add(label);
		pinnedFields.Add(null);
		pinnedObjs.Add(null);
	}

	public void UnpinVariable(int i)
	{
		pinnedLabels[i].QueueFree();
		pinnedLabels.RemoveAt(i);
		pinnedFields.RemoveAt(i);
		pinnedObjs.RemoveAt(i);
	}

	void UpdatePinnedVariables()
	{
		for (int i = 0; i < pinnedLabels.Count; i++)
		{
			var fromScript = (bool)pinnedLabels[i].GetMeta("fromScript", false);

			if (!fromScript)
			{
				if (pinnedFields[i] != null && pinnedObjs[i] != null)
				{
					pinnedLabels[i].Visible = true;
					pinnedLabels[i].Text = $"{(string)pinnedLabels[i].GetMeta("name")}: {pinnedFields[i].GetValue(pinnedObjs[i])}";
				}
				else
					pinnedLabels[i].Visible = false;
			}
		}
	}

    public void PinnedInput(InputEvent @event, int action)
    {
		if(@event is InputEventMouseButton button)
		{
			if(button.ButtonIndex == MouseButton.Left)
			{
				if (button.Pressed) pinnedAdjust = true;
				else pinnedAdjust = false;
			}
		}
		if (@event is InputEventMouseMotion eventMouseMotion)
		{
			if (!pinnedAdjust) return;
			switch (action)
			{
				case 0:
					pinnedList.Position += eventMouseMotion.Relative;
                    pinnedList.Position = new Vector2(Mathf.Clamp(pinnedList.Position.X, 0, (GetWindow().Size.X - pinnedList.Size.X/2)),
												Mathf.Clamp(pinnedList.Position.Y, buttonPinnedMove.Size.Y, (GetWindow().Size.Y - pinnedList.Size.Y/2)));
					buttonPinnedMove.GetParent<Control>().Position = pinnedList.Position - buttonPinnedMove.Size * Vector2.Down;

					break;
				case 1:
					for (int i = 0; i < pinnedLabels.Count; i++)
					{
						pinnedLabels[i].AddThemeFontSizeOverride("font_size", pinnedLabels[i].GetThemeFontSize("font_size") + Mathf.RoundToInt(eventMouseMotion.Relative.X));
					}

					break;
				default:
					break;

			}
		}
    }

    void PinnedRename(string s)
    {
        pinnedLabels[pinnedSelected].SetMeta("name", s);
        pinnedRename.Hide();
    }

    void SavePinned()
    {
        var array = new Godot.Collections.Array();
        for (int i = 0; i < pinnedLabels.Count; i++)
        {
			if (i == 0)
			{
				config.SetValue(settingsSec, "pinnedSize", pinnedLabels[0].GetThemeFontSize("font_size"));
				config.SetValue(settingsSec, "pinnedAlignment", (long)pinnedLabels[0].HorizontalAlignment);
			}

            var fromScript = (bool)pinnedLabels[i].GetMeta("fromScript", false);
			if (fromScript) continue;

            var dict = new Dictionary() {
                { "name", (string)pinnedLabels[i].GetMeta("name", "") },
                { "path", (string)pinnedLabels[i].GetMeta("path", "") },
            };
            array.Add(dict);
        }
        config.SetValue(settingsSec, "pinned", array);

		config.SetValue(settingsSec, "pinnedPosition", pinnedList.Position);
		config.SetValue(settingsSec, "pinnedMinimized", !pinnedList.Visible);

        config.Save(configPath);
    }

    void LoadPinned()
    {
		for (int i = pinnedLabels.Count - 1; i >= 0; i--)
		{
			pinnedLabels[i].QueueFree();
		}
		pinnedLabels.Clear();
		pinnedFields.Clear();
		pinnedObjs.Clear();

        var savedPinned = config.GetValue(settingsSec, "pinned", -1);

        if (savedPinned.VariantType == Variant.Type.Int) { if ((int)savedPinned == -1) { return; } }
        else
        {
            var allPinned = (Godot.Collections.Array)savedPinned;
            for (int i = 0; i < allPinned.Count; i++)
            {
                FieldInfo field = null;
                object obj = null;

                var path = (string)((Dictionary)allPinned[i])["path"];
                var nodePath = path.Split(":")[0];
                Node node = GetTree().Root.GetNodeOrNull(nodePath);
                var varPath = path.Split(":/")[1].Split("/");
                if (node != null)
                {
                    obj = node;
                    for (int v = 0; v < varPath.Length; v++)
                    {
                        field = obj.GetType().GetField(varPath[v]);
                        if (obj == null || field == null) break;
                        if (v < varPath.Length - 1)
                            obj = field.GetValue(obj);
                    }
                }

                PinVariable(path,
                            (string)((Dictionary)allPinned[i])["name"],
                            field,
                            obj);

				pinnedLabels[i].HorizontalAlignment = (HorizontalAlignment)(int)config.GetValue(settingsSec, "pinnedAlignment", 0);
				pinnedLabels[i].AddThemeFontSizeOverride("font_size", (int)config.GetValue(settingsSec, "pinnedSize", 16));
            }
        }
		pinnedList.Visible = !(bool)config.GetValue(settingsSec, "pinnedMinimized", false);
        pinnedList.Position = (Vector2)config.GetValue(settingsSec, "pinnedPosition", pinnedList.Position);
        buttonPinnedMove.GetParent<Control>().Position = pinnedList.Position - buttonPinnedMove.Size * Vector2.Down;
    }
    #endregion

    #region Menus
    void PinnedInput(InputEvent @event, Label label)
	{
		if(!content.Visible) return;
		if (@event is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
			{
				ShowPinnedMenu(label);
			}
		}
	}

	void ShowMainMenu()
	{
		mainMenu.Popup();
        mainMenu.Position = MousePosition;
    }

	void ShowPinnedMenu(Label label)
	{
		pinnedSelected = (int)label.GetMeta("index");

		pinnedMenu.Popup();
		pinnedMenu.Position = MousePosition;
    }

	void MainMenuSelect(long item)
	{
		switch (item)
		{
			case 0:
				SaveLayout();
				break;
			case 1:
				SavePinned();
				break;
			case 2:
				AddInspector();
				break;
			case 3:
				LoadLayout();
				break;
			case 4:
				LoadPinned();
				break;
			default:
				break;
		}
	}

	void PinnedMenuSelect(long item)
	{
		switch (item)
		{
			case 0:
				UnpinVariable(pinnedSelected);
				break;
			case 1:
				pinnedRename.Popup();
				pinnedRename.Position = MousePosition;
				pinnedRenameInput.Text = (string)pinnedLabels[pinnedSelected].GetMeta("name", "");
				break;
		}
	}
    #endregion

    #region Layout
    void SaveLayout()
	{
		var array = new Godot.Collections.Array();
		for (int i = 0; i < inspectors.Count; i++)
		{
			var dict = new Dictionary() {
				{ "position", inspectors[i].Position }, 
				{ "size", inspectors[i].Size }, 
				{ "scale", inspectors[i].Scale.X },
				{ "minimized", inspectors[i].Minimized},
				{ "target", inspectors[i].GetNode<DebugSuite_Inspector>("Inspector panel").NodePathCurrent } 
			};
			array.Add(dict);
		}
		config.SetValue(settingsSec, "inspectors", array);
		config.Save(configPath);
	}

	void LoadLayout()
	{
		for (int i = inspectors.Count - 1; i >= 0; i--)
		{
			inspectors[i].QueueFree();
		}
		inspectors.Clear();

		var savedLayout = config.GetValue(settingsSec, "inspectors", -1);

		if (savedLayout.VariantType == Variant.Type.Int) { if ((int)savedLayout == -1) { AddInspector(); } }
		else
		{
			var allInspectors = (Godot.Collections.Array)savedLayout;
			for (int i = 0; i < allInspectors.Count; i++)
			{
				AddInspector(
					(Vector2)((Dictionary)allInspectors[i])["position"],
					(Vector2)((Dictionary)allInspectors[i])["size"],
					(float)((Dictionary)allInspectors[i])["scale"],
					((Dictionary)allInspectors[i]).ContainsKey("target") ? (string)((Dictionary)allInspectors[i])["target"] : "",
					(bool)((Dictionary)allInspectors[i])["minimized"]
				);
			}
		}
	}
    #endregion

    #region Bookmark
	public void BookmarkNode(string path)
	{
		if (!nodeBookmarks.Contains(path))
		{
			nodeBookmarks.Add(path);
			config.SetValue(settingsSec, "nodeBookmarks", nodeBookmarks);
			config.Save(configPath);
		}
	}

	void LoadNodeBookmarks()
	{
		var bookmarks = config.GetValue(settingsSec, "nodeBookmarks", -1);
		nodeBookmarks.Clear();

		if (bookmarks.VariantType == Variant.Type.Int) { if ((int)bookmarks == -1) { return; } }
		else
		{
			var bookmarkList = (Array<string>)bookmarks;
			nodeBookmarks = bookmarkList;
		}
	}
    #endregion

    #region Rebind keys
	void RebindInput(InputEvent @event, int b)
	{
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
				rebindMenu.Popup();
				rebindMenu.Position = MousePosition;
				keyToRebind = b;
            }
        }
    }

	void RebindPressed(long item)
	{
		rebindRect.Visible = true;
		rebinding = true;
	}
    #endregion
}
