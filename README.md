# Debug Suite for Godot 4 C#

Allows you to inspect all variables in your scripts, edit them and pin them to screen.

Features:
- C# script node inspector
- Edit variables
- Pin variables to display them on screen
- Explore deeper classes and arrays
- Customize layout, add bookmarks

![DebugSuite1](https://user-images.githubusercontent.com/45795134/228032716-06afc14e-08ac-462c-b6c5-d4bcfd044c0b.jpg)

## Usage

Place the `DebugSuite` folder in your main project's folder.
Add `DebugSuite.cs` to project's `Auto Load`, the debug interface will automatically spawn when running game in debug mode.

To reference the manager in script, use `DebugSuite.Instance.manager`.

## Bookmarks

To bookmark a node path, select a node, right click on the inspector window and select the 'Bookmark current node' option.
Access bookmarks list from same menu or by right clink on node selection list.

## Variable pinning

To pin a variable to diplay, right click on a variable name in the inspector and select pin option.
You can unpin and rename the variable display by right clicking on the pinned list entry.
Save the list by going to `Debug suite` button and choosing `Save pinned`.

You can also pin a value from a script, to do so, use the `PinVariableFromScript(string customId, Variant value)` function (it must be called each time to update).

## Hotkeys

You can rebind the debug hotkeys by right clicking on the buttons on the right side of screen.

## Events

You can use events called when switching and freezing the inspectors. Useful when you want to disable game's input when in debug menu.

```cs
    #if DEBUG
	DebugSuite.Instance.manager.eventOpen += () => FreezePlayer();
	DebugSuite.Instance.manager.eventClose += () => UnfreezePlayer();
	DebugSuite.Instance.manager.eventFreeze += () => UnfreezePlayer();
	DebugSuite.Instance.manager.eventUnfreeze += () => FreezePlayer();
    #endif
```
