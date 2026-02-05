using Expedition;
using Menu;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SlugpupDaycare;

public class MiscWorldSaveDataData
{
    public Dictionary<string, List<string>> daycareSlugpups = [];
}

public static class MiscWorldSaveDataExtension
{
    private static readonly ConditionalWeakTable<MiscWorldSaveData, MiscWorldSaveDataData> cwt = new();

    public static MiscWorldSaveDataData SD(this MiscWorldSaveData miscWorldSaveData) => cwt.GetValue(miscWorldSaveData, _ => new MiscWorldSaveDataData());
}