using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Array = Godot.Collections.Array;
using System.Diagnostics;

public partial class DebugSuite_Inspector : Control
{
	DebugSuite_Manager manager;
	DebugSuite_Window window;

	//UI elements
	Control fieldTemplate;
	Button nodePathTemplate;
	PanelContainer sublistTemplate;
	Label varInfo;
	Button pathButton;

	Panel mainPanel;
	ScrollContainer scrollContainer;
	BoxContainer mainContainer;
	TextureRect foundArrow, foundArrowBck;
	Button colorTemplate;
	ColorPicker colorPicker;
	PopupPanel colorEdit;

	//Context menus
	PopupMenu menuGeneral, menuVariable;

	//Variables
	List<CanvasItem> varUpdateEveryFrame = new();

	public string nodePathCurrent;
	public string NodePathCurrent => menuCurrent == MenuType.Variables ? nodePathCurrent : "";

	string pathSelected;
	FieldInfo fieldSelected;
	object objSelected;

	string tagSymbol = "$$$";

	enum MenuType
	{
		NodePaths,
		NodeBookmarks,
		Variables
	}

	MenuType menuCurrent;

	public override void _Ready()
	{
		manager = GetNode<DebugSuite_Manager>("../../..");
		window = GetParent<DebugSuite_Window>();
		window.searchBar = window.GetNode("Search") as LineEdit;
		window.searchBar.TextChanged += Search;

		fieldTemplate = GetNode<Control>("Field Template");
		fieldTemplate.Visible = false;
		nodePathTemplate = GetNode<Button>("Node Path Template");
		nodePathTemplate.Visible = false;
		sublistTemplate = GetNode<PanelContainer>("Sublist Template");
		sublistTemplate.Visible = false;
		varInfo = GetNode<Label>("Info");
		varInfo.Visible = false;
		foundArrow = GetNode<TextureRect>("Found");
		foundArrowBck = foundArrow.Duplicate() as TextureRect;
		colorTemplate = GetNode<Button>("Color Template");
		colorEdit = GetNode<PopupPanel>("Color edit");
		colorEdit.PopupHide += ColorPickerReset;
		colorPicker = colorEdit.GetNode<ColorPicker>("ColorPicker");

		mainPanel = GetNode<Panel>("Panel");
		scrollContainer = mainPanel.GetNode<ScrollContainer>("ScrollContainer");
		mainContainer = scrollContainer.GetNode<VBoxContainer>("Container");
		mainPanel.GuiInput += PanelGuiInput;

		pathButton = mainPanel.GetNode<Button>("Path");
		pathButton.Pressed += FillPathsList;

		menuGeneral = GetNode<PopupMenu>("Menu general");
		menuGeneral.IdPressed += Bookmark;
		menuVariable = GetNode<PopupMenu>("Menu variable");
		menuVariable.IdPressed += (long item) => PinVariable(item, pathSelected, fieldSelected, objSelected);

		FillPathsList();
	}

	public override void _Process(double delta)
	{
		if (varInfo.Visible)
		{
			varInfo.GlobalPosition = GetGlobalMousePosition() + Vector2.Up * varInfo.Size.Y;
		}

		for (int i = 0; i < varUpdateEveryFrame.Count; i++)
		{
			if (IsInstanceValid(varUpdateEveryFrame[i]))
				varUpdateEveryFrame[i].QueueRedraw();
		}
	}

	#region Utility
	Type[] floatTypes = { typeof(Half), typeof(float), typeof(double) };
	Type[] intTypes = { typeof(byte), typeof(sbyte), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(short), typeof(ushort) };
	Type[] vectorFloatTypes = { typeof(Vector2), typeof(Vector3), typeof(Vector4) };
	Type[] vectorIntTypes = { typeof(Vector2I), typeof(Vector3I), typeof(Vector4I) };
	bool IsFieldArray(FieldInfo field) =>
		field.FieldType.IsArray
	 || field.FieldType == typeof(Godot.Collections.Array)
	 || field.FieldType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))
	 || field.FieldType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));

	int VectorFieldSize(FieldInfo field)
	{
		if (field.FieldType == typeof(Vector2) || field.FieldType == typeof(Vector2I))
			return 2;
		if (field.FieldType == typeof(Vector3) || field.FieldType == typeof(Vector3I))
			return 3;
		if (field.FieldType == typeof(Vector4) || field.FieldType == typeof(Vector4I))
			return 4;
		return 0;
	}

	List<string> GetAllNodePaths(Node source = null, bool scriptedOnly = true)
	{
		List<string> paths = new List<string>();

		Node node = source;
		if (source == null) node = GetTree().Root;

		foreach (Node found in node.GetChildren())
		{
			string tag = "";
			if ((Script)found.GetScript() != null)
			{
				if (found.GetScript().GetType() == typeof(GDScript))
					tag = tagSymbol + "gs";
				else
					tag = tagSymbol + "s";
			}

			if (tag != "" && scriptedOnly)
				paths.Add(found.GetPath() + tag);
			else if (!scriptedOnly)
				paths.Add(found.GetPath() + tag);

			if (found.GetChildCount() > 0) paths.AddRange(GetAllNodePaths(found));
		}

		return paths;
	}

	Vector4 GetVector(FieldInfo field, object obj)
	{
		var value = field.GetValue(obj);
		if (field.FieldType == typeof(Vector2))
		{
			var vec = (Vector2)value;
			return new Vector4(vec.X, vec.Y, 0, 0);
		}
		if (field.FieldType == typeof(Vector2I))
		{
			var vec = (Vector2I)value;
			return new Vector4(vec.X, vec.Y, 0, 0);

		}
		if (field.FieldType == typeof(Vector3))
		{
			var vec = (Vector3)value;
			return new Vector4(vec.X, vec.Y, vec.Z, 0);

		}
		if (field.FieldType == typeof(Vector3I))
		{
			var vec = (Vector3I)value;
			return new Vector4(vec.X, vec.Y, vec.Z, 0);
		}
		if (field.FieldType == typeof(Vector4))
		{
			var vec = (Vector4)value;
			return new Vector4(vec.X, vec.Y, vec.Z, vec.W);

		}
		if (field.FieldType == typeof(Vector4I))
		{
			var vec = (Vector4I)value;
			return new Vector4(vec.X, vec.Y, vec.Z, vec.W);
		}
		return Vector4.Zero;
	}

	double GetVectorValue(Vector4 v, int pos)
	{
		switch (pos)
		{
			case 0:
				return v.X;
			case 1:
				return v.Y;
			case 2:
				return v.Z;
			case 3:
				return v.W;
		}
		return 0;
	}

	string GetListItemName(object obj)
	{
		var fields = obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
		if (fields == null) return "null";
		if (fields.Length == 0) return "null";
		for (int i = 0; i < fields.Length; i++)
		{
			var fieldName = fields[i].Name.ToLower();
			if (fieldName.Contains("name") || fieldName == "id" || fieldName.EndsWith("id"))
				return fields[i].GetValue(obj).ToString();
		}
		if (fields[0].GetValue(obj) == null) return "null";
		return fields[0].GetValue(obj).ToString();
	}

	void ColorPickerReset() //Where's option to disconnect all signals, huh?
	{
		var signals = colorPicker.GetSignalConnectionList("color_changed");
		for (int i = 0; i < signals.Count; i++)
		{
			colorPicker.Disconnect("color_changed", (Callable)signals[i]["callable"]);
		}
	}
	#endregion

	#region Interface
	void ClearList(Control parent, bool clearVars)
	{
		foreach (var item in parent.GetChildren())
		{
			item.QueueFree();
		}
		if (clearVars)
		{
			varUpdateEveryFrame.Clear();
		}
	}

	void FillPathsList()
	{
		ClearList(mainContainer, true);

		pathButton.Text = "Select node path";
		menuCurrent = MenuType.NodePaths;

		var allPaths = GetAllNodePaths();
		for (int i = 0; i < allPaths.Count; i++)
		{
			int idx = i;
			var nodePathInstance = nodePathTemplate.Duplicate() as Button;
			mainContainer.AddChild(nodePathInstance);
			nodePathInstance.Visible = true;

			nodePathInstance.Text = allPaths[i].Replace("/root/", "").Split(tagSymbol)[0];
			nodePathInstance.Pressed += () => SelectNode(allPaths[idx].Split(tagSymbol)[0]);
			nodePathInstance.GuiInput += PathGuiInput;
		}
		window.searchBar.Text = "";
	}

	public void SelectNode(string path)
	{
		var node = GetTree().Root.GetNodeOrNull(path);
		if (node == null) return;

		FillFieldList(node, mainContainer, true, path + ":");
		pathButton.Text = path;
		nodePathCurrent = path;
		window.searchBar.Text = "";
	}

	void FillFieldList(object obj, Control parent, bool clearVars, string path)
	{
		ClearList(parent, clearVars);

		menuCurrent = MenuType.Variables;

		var fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		for (int i = 0; i < fields.Length; i++)
		{
			CreateField(fields[i], obj, parent, path);
		}
	}

	void FillArrayList(System.Collections.IList list, FieldInfo field, object obj, Control parent, string path)
	{
		string pathAdd = path + "/" + field.Name;

		ClearList(parent, false);

		for (int i = 0; i < list.Count; i++)
		{
			int idx = i;

			if (list[i].GetType().IsPrimitive || list[i].GetType() == typeof(string))
			{
				CreateFieldArrayItem(field, obj, list, parent, i, path);
			}
			else
			{
				var b = new Button();
				parent.AddChild(b);
				b.Alignment = HorizontalAlignment.Left;
				if (list[i] != null)
					b.Text = $"[{i}] {GetListItemName(list[i])}";
				else
					b.Text = "null";

				b.ToggleMode = true;
				b.Toggled += (bool pressed) => ClassExpandArray(pressed, b, list[idx], obj, parent, path + "#" + idx.ToString());
			}
		}
	}

	void CreateField(FieldInfo field, object obj, Control parent, string path)
	{
		if (field.Name == "NativePtr") return;

		string pathAdd = path + "/" + field.Name;

		var fieldInfoInstance = fieldTemplate.Duplicate() as Control;
		parent.AddChild(fieldInfoInstance);
		fieldInfoInstance.Name = field.Name;

		fieldInfoInstance.Visible = true;

		var label = fieldInfoInstance.GetNode<Label>("Name");
		label.Text = field.Name;
		label.MouseEntered += () => ShowVarInfo(field);
		label.MouseExited += HideVarInfo;
		label.GuiInput += (InputEvent e) => VariableGuiInput(e, pathAdd, field, obj);
		label.SetMeta("path", pathAdd);

		//Number
		if (floatTypes.Contains(field.FieldType) || intTypes.Contains(field.FieldType))
		{
			var prop = new SpinBox();
			prop.AllowGreater = true;
			prop.AllowLesser = true;
			prop.Step = 0.001;
			fieldInfoInstance.AddChild(prop);

			prop.Value = (double)Convert.ChangeType(field.GetValue(obj), typeof(double));
			if (intTypes.Contains(field.GetType()))
			{
				prop.Step = 1;
				prop.Rounded = true;
			}
			prop.ValueChanged += (double number) => VarEditNumber(number, field, obj);
			prop.Draw += () => UpdateNumber(prop, field, obj);
			varUpdateEveryFrame.Add(prop);
		}
		//Vector
		else if (vectorFloatTypes.Contains(field.FieldType) || vectorIntTypes.Contains(field.FieldType))
		{
			var size = VectorFieldSize(field);
			for (int i = 0; i < size; i++)
			{
				int idx = i;

				var prop = new SpinBox();
				fieldInfoInstance.AddChild(prop);

				if (vectorIntTypes.Contains(field.GetType()))
				{
					prop.Step = 1;
					prop.Rounded = true;
				}
				prop.Value = GetVectorValue(GetVector(field, obj), i);
				prop.ValueChanged += (double number) => VarEditVector(number, idx, field, obj);
				prop.Draw += () => UpdateVector(prop, idx, field, obj);
				varUpdateEveryFrame.Add(prop);
			}
		}
		//Bool
		else if (field.FieldType == typeof(bool))
		{
			var prop = new CheckBox();
			fieldInfoInstance.AddChild(prop);

			prop.ButtonPressed = (bool)Convert.ChangeType(field.GetValue(obj), typeof(bool));
			prop.Toggled += (bool check) => VarEditBool(check, field, obj);
			prop.Draw += () => UpdateBool(prop, field, obj);
			varUpdateEveryFrame.Add(prop);
		}
		//String
		else if (field.FieldType == typeof(string))
		{
			var prop = new LineEdit();
			fieldInfoInstance.AddChild(prop);

			prop.Text = (string)field.GetValue(obj);
			prop.TextSubmitted += (string text) => VarEditString(text, field, obj);
			prop.MouseEntered += () => UpdateString(prop, field, obj);
		}
		//Color
		else if (field.FieldType == typeof(Color))
		{
			var prop = colorTemplate.Duplicate() as Button;
			fieldInfoInstance.AddChild(prop);
			prop.Visible = true;

			prop.Modulate = (Color)field.GetValue(obj);
			prop.Pressed += () => { 
				colorEdit.PopupCentered();
				colorPicker.Color = prop.Modulate;
				colorPicker.ColorChanged += (Color c) => VarEditColor(c, field, obj);
				};
			prop.Draw += () => UpdateColor(prop, field, obj);
			varUpdateEveryFrame.Add(prop);
		}
		//Array
		else if (IsFieldArray(field))
		{
			var prop = new Button();
			fieldInfoInstance.AddChild(prop);

			prop.Text = "[]+";
			prop.ToggleMode = true;
			prop.Toggled += (bool pressed) => ArrayExpand(pressed, prop, field, obj, parent, pathAdd);
		}
		//Class
		else
		{
			var prop = new Button();
			fieldInfoInstance.AddChild(prop);

			var value = field.GetValue(obj);
			if (value == null)
			{
				prop.Text = "null";
				prop.Disabled = true;
			}
			else
			{
				prop.Text = "+";
				prop.ToggleMode = true;
				prop.Toggled += (bool pressed) => ClassExpand(pressed, prop, field, obj, parent, pathAdd);
			}
		}
	}

	void CreateFieldArrayItem(FieldInfo field, object objParent, object objArray, Control parent, int idx, string path)
	{
		string pathAdd = path + "#" + idx.ToString();

		var fieldInfoInstance = fieldTemplate.Duplicate() as Control;
		parent.AddChild(fieldInfoInstance);
		fieldInfoInstance.Name = field.Name;

		fieldInfoInstance.Visible = true;

		var label = fieldInfoInstance.GetNode<Label>("Name");
		label.Text = $"[{idx}]";
		label.GuiInput += (InputEvent e) => VariableGuiInput(e, pathAdd, field, objParent);
		label.SetMeta("path", pathAdd);

		var value = ((System.Collections.IList)objArray)[idx];

		//Number
		if (floatTypes.Contains(value.GetType()) || intTypes.Contains(value.GetType()))
		{
			var prop = new SpinBox();
			prop.AllowGreater = true;
			prop.AllowLesser = true;
			prop.Step = 0.001;
			fieldInfoInstance.AddChild(prop);

			if (intTypes.Contains(value.GetType()))
			{
				prop.Rounded = true;
				prop.Step = 1;
			}

			prop.Value = (double)Convert.ChangeType(value, typeof(double));
			prop.ValueChanged += (double number) => VarEditNumber(number, field, objParent, idx);
			prop.Draw += () => UpdateNumber(prop, field, objParent, idx);
			varUpdateEveryFrame.Add(prop);
		}
		//String
		else if (value.GetType() == typeof(string))
		{
			var prop = new LineEdit();
			prop.ExpandToTextLength = true;
			fieldInfoInstance.AddChild(prop);

			prop.Text = (string)Convert.ChangeType(value, typeof(string));
			prop.TextSubmitted += (string text) => VarEditString(text, field, objParent, idx);
			prop.MouseEntered += () => UpdateString(prop, field, objParent, idx);
		}
		//Bool
		else if (value.GetType() == typeof(bool))
		{
			var prop = new CheckBox();
			fieldInfoInstance.AddChild(prop);

			prop.ButtonPressed = (bool)Convert.ChangeType(value, typeof(bool));
			prop.Toggled += (bool text) => VarEditBool(text, field, objParent, idx);
			prop.Draw += () => UpdateBool(prop, field, objParent, idx);
			varUpdateEveryFrame.Add(prop);
        }
        //Color
        else if (value.GetType() == typeof(Color))
        {
            var prop = colorTemplate.Duplicate() as Button;
            fieldInfoInstance.AddChild(prop);
			prop.Modulate = (Color)Convert.ChangeType(value, typeof(Color));
            prop.Visible = true;
            prop.Pressed += () => {
                colorEdit.PopupCentered();
                colorPicker.Color = prop.Modulate;
                colorPicker.ColorChanged += (Color c) => VarEditColor(c, field, objParent, idx);
            };
            prop.Draw += () => UpdateColor(prop, field, objParent, idx);
            varUpdateEveryFrame.Add(prop);
        }
        return;

	}

	void ShowVarInfo(FieldInfo field)
	{
		varInfo.Visible = true;
		varInfo.Text = field.FieldType.ToString();
	}

	void HideVarInfo()
	{
		varInfo.Visible = false;
	}

	void PanelGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Right)
			{
				if (menuCurrent == MenuType.Variables)
				{
					menuGeneral.Position = Vector2I.Zero;
					menuGeneral.Popup();
					menuGeneral.Position = manager.MousePosition;
				}
				if (menuCurrent == MenuType.NodePaths)
				{
					ShowBookmarks();
				}
			}
		}
	}

	void VariableGuiInput(InputEvent @event, string path, FieldInfo field, object obj)
	{
		if (@event is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Right)
			{
				if (menuCurrent == MenuType.Variables)
				{
					pathSelected = path;
					fieldSelected = field;
					objSelected = obj;

					menuVariable.Position = Vector2I.Zero;
					menuVariable.Popup();
					menuVariable.Position = manager.MousePosition;
				}
			}
		}
	}

	void PathGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse)
		{
			if (mouse.ButtonIndex == MouseButton.Right)
			{
				ShowBookmarks();
			}
		}
	}

	void PinVariable(long item, string path, FieldInfo field, object obj)
	{
		if (item == 0)
		{
			manager.PinVariable(path, path.Replace("/root/", ""), field, obj); //TODO: variable path
		}
	}

	void Bookmark(long item)
	{
		switch (item)
		{
			case 0:
				ShowBookmarks();
				break;
			case 1:
				manager.BookmarkNode(nodePathCurrent);
				break;
			default:
				break;
		}
	}

	void ShowBookmarks()
	{
		ClearList(mainContainer, true);
		pathButton.Text = "Bookmarks";
		for (int i = 0; i < manager.nodeBookmarks.Count; i++)
		{
			int idx = i;
			Button bookmark = nodePathTemplate.Duplicate() as Button;
			bookmark.Text = manager.nodeBookmarks[i];
			bookmark.Pressed += () => SelectNode(manager.nodeBookmarks[idx]);
			bookmark.Visible = true;
			mainContainer.AddChild(bookmark);
		}
	}

	void Search(string text)
	{
		if (!IsInstanceValid(foundArrow)) //Restore arrow when it's deleted
		{
			foundArrow = foundArrowBck.Duplicate() as TextureRect;
			window.AddChild(foundArrow);
		}
		if (text == "") 
		{
			foundArrow.Visible = false;
			return;
		}
		switch (menuCurrent)
		{
			case MenuType.NodePaths:
				foreach (var item in mainContainer.GetChildren())
				{
					if (((Button)item).Text.ToLower().Contains(text))
					{
						scrollContainer.ScrollVertical = (item.GetIndex()-1) * ((int)((Control)item).Size.Y + mainContainer.GetThemeConstant("separation"));
						foundArrow.Visible = true;
						foundArrow.Reparent(item);
						foundArrow.Position = Vector2.Zero;
						item.MoveChild(foundArrow, 0);
						return;
					}
					foundArrow.Visible = false;
				}
				break;
			case MenuType.Variables:
				foreach (var item in mainContainer.GetChildren())
				{
					if (item.Name.ToString().ToLower().Contains(text)) //Copied code but whatevs...
					{
						scrollContainer.ScrollVertical = (item.GetIndex()-1) * ((int)((Control)item).Size.Y + mainContainer.GetThemeConstant("separation"));
						foundArrow.Visible = true;
						foundArrow.TopLevel = false;
						foundArrow.Reparent(item);
						item.MoveChild(foundArrow, 0);
						return;
                    }
					foundArrow.Visible = false;
				}
				break;
			default:
				break;
		}
		return;			
	}
    #endregion

    #region Variable update
    void UpdateNumber(SpinBox prop, FieldInfo field, object obj, int arrayIdx = -1)
	{
		if (arrayIdx == -1)
		{
			prop.Value = (double)Convert.ChangeType(field.GetValue(obj), typeof(double));
		}
		else
		{
			var value = (System.Collections.IList)field.GetValue(obj);
			prop.Value = (double)Convert.ChangeType(value[arrayIdx], typeof(double));
		}
	}

    void UpdateVector(SpinBox prop, int pos, FieldInfo field, object obj)
	{
		prop.Value = GetVectorValue(GetVector(field, obj), pos);
	}

	void UpdateBool(CheckBox prop, FieldInfo field, object obj, int arrayIdx = -1)
	{
		if (arrayIdx == -1)
		{
			prop.ButtonPressed = (bool)Convert.ChangeType(field.GetValue(obj), typeof(bool));
		}
		else
		{
			var value = (System.Collections.IList)field.GetValue(obj);
			prop.ButtonPressed = (bool)Convert.ChangeType(value[arrayIdx], typeof(bool));
		}
	}

	void UpdateString(LineEdit prop, FieldInfo field, object obj, int arrayIdx = -1)
	{
		if (arrayIdx == -1)
		{
			prop.Text = (string)field.GetValue(obj);
		}
		else
		{
			var value = (System.Collections.IList)field.GetValue(obj);
			prop.Text = (string)Convert.ChangeType(value[arrayIdx], typeof(string));
		}
	}

	void UpdateColor(Button prop, FieldInfo field, object obj, int arrayIdx = -1)
	{
		if (arrayIdx == -1)
		{
			prop.Modulate = (Color)field.GetValue(obj);
		}
		else
		{
			var value = (System.Collections.IList)field.GetValue(obj);
			prop.Modulate = (Color)Convert.ChangeType(value[arrayIdx], typeof(Color));
		}
	}
    #endregion

    #region Variable edit
	void ClassExpand(bool pressed, Button button, FieldInfo field, object obj, Control parent, string path)
	{
		if (pressed)
		{
			var value = field.GetValue(obj);
			if (value == null) return;

			var sublistInstance = sublistTemplate.Duplicate() as Control;
			parent.AddChild(sublistInstance);
			sublistInstance.Visible = true;
			parent.MoveChild(sublistInstance, button.GetParent().GetIndex() + 1);

			FillFieldList(value, sublistInstance.GetNode<Control>("Container"), false, path);
			button.Text = "-";
			button.SetMeta("panel", sublistInstance);
		}
		else
		{
			var panel = (Node)button.GetMeta("panel");
			panel.QueueFree();

			button.Text = "+";
		}
	}

	void ClassExpandArray(bool pressed, Button button, object value, object obj, Control parent, string path)
	{
		if (pressed)
		{
			if (value == null) return;

			var sublistInstance = sublistTemplate.Duplicate() as Control;
			parent.AddChild(sublistInstance);
			sublistInstance.Visible = true;
			parent.MoveChild(sublistInstance, button.GetIndex() + 1);

			FillFieldList(value, sublistInstance.GetNode<Control>("Container"), false, path);
			button.SetMeta("panel", sublistInstance);
		}
		else
		{
			var panel = (Node)button.GetMeta("panel");
			panel.QueueFree();
		}
	}

	void ArrayExpand(bool pressed, Button button, FieldInfo field, object obj, Control parent, string path)
	{
		if (pressed)
		{
			var value = (System.Collections.IList)field.GetValue(obj);
			if (value == null) return;
			if (value.Count == 0) return;

			var sublistInstance = sublistTemplate.Duplicate() as Control;
			parent.AddChild(sublistInstance);
			sublistInstance.Visible = true;
			parent.MoveChild(sublistInstance, button.GetParent().GetIndex() + 1);

			FillArrayList(value, field, obj, sublistInstance.GetNode<Control>("Container"), path);
			button.Text = "[]-";
			button.SetMeta("panel", sublistInstance);
		}
		else
		{
			if (!button.HasMeta("panel")) return;

			var panel = (Node)button.GetMeta("panel");
			panel.QueueFree();

			button.Text = "[]+";
		}
	}

	public void VarEditNumber(double number, FieldInfo field, object obj, int arrayIdx = -1)
	{
		try
		{
			if (arrayIdx == -1)
			{
				field.SetValue(obj, Convert.ChangeType(number, field.FieldType));
			}
			else
			{
				var array = (System.Collections.IList)field.GetValue(obj);
				array[arrayIdx] = Convert.ChangeType(number, array[0].GetType());
				field.SetValue(obj, array);
			}
		}
		catch(Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
    }

    void VarEditVector(double number, int pos, FieldInfo field, object obj, int arrayIdx = -1)
	{
		try
		{
			if (arrayIdx == -1)
			{
				var value = field.GetValue(obj);
				//Hell.
				if (field.FieldType == typeof(Vector2))
				{
					var vec = (Vector2)value;
					var num = (float)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector2(num, vec.Y)); break;
						case 1:
							field.SetValue(obj, new Vector2(vec.X, num)); break;
						case 2:
							field.SetValue(obj, new Vector2(vec.X, vec.Y)); break;
					}
				}
				if (field.FieldType == typeof(Vector2I))
				{
					var vec = (Vector2I)value;
					var num = (int)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector2I(num, vec.Y)); break;
						case 1:
							field.SetValue(obj, new Vector2I(vec.X, num)); break;
						case 2:
							field.SetValue(obj, new Vector2I(vec.X, vec.Y)); break;
					}
				}
				if (field.FieldType == typeof(Vector3))
				{
					var vec = (Vector3)value;
					var num = (float)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector3(num, vec.Y, vec.Z)); break;
						case 1:
							field.SetValue(obj, new Vector3(vec.X, num, vec.Z)); break;
						case 2:
							field.SetValue(obj, new Vector3(vec.X, vec.Y, num)); break;
					}
				}
				if (field.FieldType == typeof(Vector3I))
				{
					var vec = (Vector3I)value;
					var num = (int)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector3I(num, vec.Y, vec.Z)); break;
						case 1:
							field.SetValue(obj, new Vector3I(vec.X, num, vec.Z)); break;
						case 2:
							field.SetValue(obj, new Vector3I(vec.X, vec.Y, num)); break;
					}
				}
				if (field.FieldType == typeof(Vector4))
				{
					var vec = (Vector4)value;
					var num = (float)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector4(num, vec.Y, vec.Z, vec.W)); break;
						case 1:
							field.SetValue(obj, new Vector4(vec.X, num, vec.Z, vec.W)); break;
						case 2:
							field.SetValue(obj, new Vector4(vec.X, vec.Y, num, vec.W)); break;
						case 3:
							field.SetValue(obj, new Vector4(vec.X, vec.Y, vec.Z, num)); break;
					}
				}
				if (field.FieldType == typeof(Vector4I))
				{
					var vec = (Vector4I)value;
					var num = (int)number;
					switch (pos)
					{
						case 0:
							field.SetValue(obj, new Vector4I(num, vec.Y, vec.Z, vec.W)); break;
						case 1:							  
							field.SetValue(obj, new Vector4I(vec.X, num, vec.Z, vec.W)); break;
						case 2:							  
							field.SetValue(obj, new Vector4I(vec.X, vec.Y, num, vec.W)); break;
						case 3:							  
							field.SetValue(obj, new Vector4I(vec.X, vec.Y, vec.Z, num)); break;
					}
				}
			}
			else
			{
                var value = field.GetValue(obj);
                var array = (System.Collections.IList)field.GetValue(obj);
                //Mega hell.
                if (field.FieldType == typeof(Vector2))
                {
                    var vec = (Vector2)value;
                    var num = (float)number;
                    switch (pos)
                    {
                        case 0:
							array[arrayIdx] = Convert.ChangeType(new Vector2(num, vec.Y), array[0].GetType());
							break;
                        case 1:
							array[arrayIdx] = Convert.ChangeType(new Vector2(vec.X, num), array[0].GetType());
							break;
                        case 2:
							array[arrayIdx] = Convert.ChangeType(new Vector2(vec.X, vec.Y), array[0].GetType());
							break;
                    }
                    field.SetValue(obj, array);
                }
                if (field.FieldType == typeof(Vector2I))
                {
                    var vec = (Vector2I)value;
                    var num = (int)number;
                    switch (pos)
                    {
                        case 0:
                            array[arrayIdx] = Convert.ChangeType(new Vector2I(num, vec.Y), array[0].GetType());
                            break;
                        case 1:
                            array[arrayIdx] = Convert.ChangeType(new Vector2I(vec.X, num), array[0].GetType());
                            break;
                        case 2:
                            array[arrayIdx] = Convert.ChangeType(new Vector2I(vec.X, vec.Y), array[0].GetType());
                            break;
                    }
                    field.SetValue(obj, array);
                }
                if (field.FieldType == typeof(Vector3))
                {
                    var vec = (Vector3)value;
                    var num = (float)number;
                    switch (pos)
                    {
                        case 0:
                            array[arrayIdx] = Convert.ChangeType(new Vector3(num, vec.Y, vec.Z), array[0].GetType());
							break;
                        case 1:
                            array[arrayIdx] = Convert.ChangeType(new Vector3(vec.X, num, vec.Z), array[0].GetType());
							break;
                        case 2:
                            array[arrayIdx] = Convert.ChangeType(new Vector3(vec.X, vec.Y, num), array[0].GetType());
							break;
                    }
                    field.SetValue(obj, array);
                }
                if (field.FieldType == typeof(Vector3I))
                {
                    var vec = (Vector3I)value;
                    var num = (int)number;
                    switch (pos)
                    {
                        case 0:
                            array[arrayIdx] = Convert.ChangeType(new Vector3I(num, vec.Y, vec.Z), array[0].GetType());
							break;
                        case 1:
                            array[arrayIdx] = Convert.ChangeType(new Vector3I(vec.X, num, vec.Z), array[0].GetType());
							break;
                        case 2:
                            array[arrayIdx] = Convert.ChangeType(new Vector3I(vec.X, vec.Y, num), array[0].GetType());
							break;
                    }
                    field.SetValue(obj, array);
                }
                if (field.FieldType == typeof(Vector4))
                {
                    var vec = (Vector4)value;
                    var num = (float)number;
                    switch (pos)
                    {
                        case 0:
                            array[arrayIdx] = Convert.ChangeType(new Vector4(num, vec.Y, vec.Z, vec.W), array[0].GetType());
							break;										   
                        case 1:											   
                            array[arrayIdx] = Convert.ChangeType(new Vector4(vec.X, num, vec.Z, vec.W), array[0].GetType());
							break;										   
                        case 2:											   
                            array[arrayIdx] = Convert.ChangeType(new Vector4(vec.X, vec.Y, num, vec.W), array[0].GetType());
							break;										   
                        case 3:											   
                            array[arrayIdx] = Convert.ChangeType(new Vector4(vec.X, vec.Y, vec.Z, num), array[0].GetType());
							break;										   
                    }													   
                    field.SetValue(obj, array);							   
                }														   
                if (field.FieldType == typeof(Vector4I))				   
                {														   
                    var vec = (Vector4I)value;							   
                    var num = (int)number;								   
                    switch (pos)										   
                    {													   
                        case 0:											   
                            array[arrayIdx] = Convert.ChangeType(new Vector4I(num, vec.Y, vec.Z, vec.W), array[0].GetType());
							break;										   					   
                        case 1:											   					   
                            array[arrayIdx] = Convert.ChangeType(new Vector4I(vec.X, num, vec.Z, vec.W), array[0].GetType());
							break;										   					   
                        case 2:											   					   
                            array[arrayIdx] = Convert.ChangeType(new Vector4I(vec.X, vec.Y, num, vec.W), array[0].GetType());
							break;										   					  
                        case 3:											   					  
                            array[arrayIdx] = Convert.ChangeType(new Vector4I(vec.X, vec.Y, vec.Z, num), array[0].GetType());
							break;
                    }
                    field.SetValue(obj, array);
                }
            }
		}
		catch(Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
    }

    void VarEditBool(bool check, FieldInfo field, object obj, int arrayIdx = -1)
	{
		try
		{
			if (arrayIdx == -1)
			{
				field.SetValue(obj, Convert.ChangeType(check, field.FieldType));
			}
			else
			{
				var array = (System.Collections.IList)field.GetValue(obj);
				array[arrayIdx] = Convert.ChangeType(check, array[0].GetType());
				field.SetValue(obj, array);
			}
		}
		catch(Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
    }

    void VarEditString(string text, FieldInfo field, object obj, int arrayIdx = -1)
	{
		try
		{
			if (arrayIdx == -1)
			{
				field.SetValue(obj, text);
			}
			else
			{
				var array = (System.Collections.IList)field.GetValue(obj);
				array[arrayIdx] = Convert.ChangeType(text, array[0].GetType());
				field.SetValue(obj, array);
			}
		}
		catch(Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
    }

    void VarEditColor(Color color, FieldInfo field, object obj, int arrayIdx = -1)
	{
		try
		{
			if (arrayIdx == -1)
			{
				field.SetValue(obj, color);
			}
			else
			{
				var array = (System.Collections.IList)field.GetValue(obj);
				array[arrayIdx] = Convert.ChangeType(color, array[0].GetType());
				field.SetValue(obj, array);
			}
		}
		catch(Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
    }
    #endregion
}
