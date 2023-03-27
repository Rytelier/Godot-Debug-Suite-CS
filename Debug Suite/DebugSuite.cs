using Godot;
using System;

public partial class DebugSuite : Node
{
    static DebugSuite instance;

    public static DebugSuite Instance
    {
        get => instance;
    }

    public DebugSuite()
    {
        instance = this;
    }

    string uiScenePath = "res://Debug Suite/Resources/Debug suite.tscn";
	public DebugSuite_Manager manager;

	public override void _Ready()
	{
        #if DEBUG
        Callable.From(InstanceScene).CallDeferred();
        #endif
    }

	void InstanceScene()
	{
		manager = GD.Load<PackedScene>(uiScenePath).Instantiate() as DebugSuite_Manager;

		GetTree().Root.AddChild(manager);
	}

    public void PinVariableFromScript(string customId, Variant value)
    {
        #if DEBUG
        manager.PinVariableFromScript(customId, value);
        #endif
    }
}