using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

public class TestClass {
    public unsafe void Test() {
        var c = UIState.Instance()->MassivePcContentTodo.Director;
        if (c != null) {
            var todo = c->MassivePcContentTodos[1];
            if (todo[1].Enabled) {
                var t = todo[1];
                var timeRemaining = t.EndTimestamp - Framework.GetServerTime();
            }
        }
    }
}
