using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RePlate.Game;

/// <summary>Clicks that go through a button's own registered click event, the same one a mouse click fires.</summary>
public static unsafe class Clicks
{
    /// <summary>Clicks the window's button whose own click event carries this number.</summary>
    public static bool ButtonWithParam(AtkUnitBase* addon, int param)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            var component = node == null ? null : node->GetAsAtkComponentNode();
            if (component == null || component->Component == null || component->Component->GetComponentType() != ComponentType.Button)
                continue;
            for (var evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
                if (evt->State.EventType == AtkEventType.ButtonClick && evt->Param == param && evt->Listener != null)
                    return Button((AtkComponentButton*)component->Component, param);
        }
        return false;
    }

    /// <summary>Sends the button's own registered click, when it carries this number, to whoever listens for it.</summary>
    public static bool Button(AtkComponentButton* button, int param)
    {
        if (button == null || !button->IsEnabled || button->OwnerNode == null) return false;
        var node = &button->OwnerNode->AtkResNode;
        for (var evt = node->AtkEventManager.Event; evt != null; evt = evt->NextEvent)
        {
            if (evt->State.EventType != AtkEventType.ButtonClick || evt->Listener == null || evt->Param != param) continue;
            var copy = *evt;
            var data = new AtkEventData();
            evt->Listener->ReceiveEvent(AtkEventType.ButtonClick, param, &copy, &data);
            return true;
        }
        return false;
    }

    /// <summary>The first list in the window, for menus that have just one.</summary>
    public static AtkComponentList* FirstList(AtkUnitBase* addon)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            var component = node == null ? null : node->GetAsAtkComponentNode();
            if (component != null && component->Component != null && component->Component->GetComponentType() == ComponentType.List)
                return (AtkComponentList*)component->Component;
        }
        return null;
    }
}
