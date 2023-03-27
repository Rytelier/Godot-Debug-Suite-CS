using Godot;
using System;

public partial class DebugSuite_Window : Control
{
    public delegate void WindowEvent();
    public WindowEvent OnDelete;

    public Control container;

    [Export] Vector2 panelScaleRange = new Vector2(0.5f, 1.5f);

    Button moveButton, scaleButton, resizeButton;
    bool move, scale, resize;

    PopupMenu deleteConfirm;
    Button deleteButton;
    bool deleteMouseOut;

    public LineEdit searchBar;
    Button closeButton, minimizeButton;
    public bool Minimized => !container.Visible;

    float rectX;

    Vector2 posMax, posDef, sizeDef;

    Control freezer;

    public override void _Input(InputEvent @event)
    {
        if (move)
        {
            if (@event is InputEventMouseMotion eventMouseMotion)
            {

                Position += eventMouseMotion.Relative;
                Position = new Vector2(Mathf.Clamp(Position.X, 0, (posMax.X - Size.X/2) / Scale.X),
                                            Mathf.Clamp(Position.Y, 0, (posMax.Y - Size.Y/2) / Scale.Y));
            }
        }
        if (resize)
        {
            if (@event is InputEventMouseMotion eventMouseMotion)
            {
                Size += eventMouseMotion.Relative / Scale;
                Size = new Vector2(Mathf.Clamp(Size.X, 0, (posMax.X - Position.X) / Scale.X),
                                        Mathf.Clamp(Size.Y, 0, (posMax.Y - Position.Y) / Scale.Y));
            }
        }
        if (scale)
        {
            if (@event is InputEventMouseMotion eventMouseMotion)
            {
                Scale += Vector2.One * eventMouseMotion.Relative.X * 0.01f;
                float scale = Mathf.Clamp(Scale.X, panelScaleRange.X, panelScaleRange.Y);
                Scale = new Vector2(scale, scale);
            }
        }
    }

    public override void _Ready()
    {
        container = GetNode("Inspector panel") as Control;

        moveButton = GetNode("Top/Move") as Button;
        scaleButton = GetNode("Top/Scale") as Button;
        resizeButton = GetNode("Resize") as Button;
        moveButton.ButtonDown += () => PanelTransform(0, true);
        moveButton.ButtonUp += () => PanelTransform(0, false);
        moveButton.GuiInput += (InputEvent e) => PanelReset(e, 0);
        scaleButton.ButtonDown += () => PanelTransform(1, true);
        scaleButton.ButtonUp += () => PanelTransform(1, false);
        scaleButton.GuiInput += (InputEvent e) => PanelReset(e, 1);
        resizeButton.ButtonDown += () => PanelTransform(2, true);
        resizeButton.ButtonUp += () => PanelTransform(2, false);
        resizeButton.GuiInput += (InputEvent e) => PanelReset(e, 2);

        deleteConfirm = GetNode<PopupMenu>("Delete confirm");
        deleteConfirm.IdPressed += (_) => Delete();

        minimizeButton = GetNode("Top2/Minimize") as Button;
        closeButton = GetNode("Top2/Close") as Button;
        minimizeButton.ButtonDown += () => SwitchWindow(0);
        closeButton.ButtonDown += () => SwitchWindow(1);

        searchBar = GetNode("Search") as LineEdit;

        freezer = GetNode<Control>("Freezer");

        posMax = GetWindow().Size;
        posDef = Position;
        sizeDef = Size;
    }

    public void PanelTransform(int transform, bool start)
    {
        switch (transform)
        {
            case 0:
                move = start;
                break;
            case 1:
                scale = start;
                break;
            case 2:
                resize = start;
                break;
        }
    }

    public void PanelReset(InputEvent InputEvent, int transform)
    {
        if (InputEvent is InputEventMouseButton && InputEvent.IsPressed())
        {
            if (((InputEventMouseButton)InputEvent).ButtonIndex == MouseButton.Right)
            {
                switch (transform)
                {
                    case 0:
                        Position = posDef;
                        Scale = new Vector2(1, 1);
                        Size = sizeDef;
                        break;
                    case 1:
                        Scale = new Vector2(1, 1);
                        break;
                    case 2:
                        Size = sizeDef;
                        break;
                }
            }
        }
    }

    public void SwitchWindow(int set)
    {
        switch (set)
        {
            case 0:
                if (container.Visible)
                {
                    rectX = Size.X;
                    Size = new Vector2(200, Size.Y);
                }
                else
                {
                    Size = new Vector2(rectX, Size.Y);
                }
                container.Visible = !container.Visible;
                resizeButton.Visible = container.Visible;
                break;
            case 1:
                deleteMouseOut = true;
                deleteConfirm.Popup();
                deleteConfirm.Position = new Vector2I((int)GetWindow().GetMousePosition().X, (int)GetWindow().GetMousePosition().Y);
                break;
        }
    }

    public void Delete()
    {
        OnDelete?.Invoke();
        QueueFree();
    }

    public void Freeze(bool freeze)
    {
        if (freeze) 
        {
            if (Visible) //Remove focus
            {
                Hide();
                Show();
            }

            if(container.Visible)
                freezer.Visible = true;
        }
        else
        {
            freezer.Visible = false;
        }
        moveButton.Visible = !freeze;
        scaleButton.Visible = !freeze;
        closeButton.Visible = !freeze;
        minimizeButton.Visible = !freeze;
        searchBar.Visible = !freeze;
        if (container.Visible) resizeButton.Visible = !freeze;
    }
}
