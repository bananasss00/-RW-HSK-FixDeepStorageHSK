using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixDeepStorageHSK
{
    public static class ListerHaulablesThreaded
    {
        private const int PatchPriority = Priority.First;
        private const int ThreadUpdateInterval = 60;
        private static Thread _thread;
        private static readonly object Locker = new object();

        public static void InitPatches(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(ListerHaulables), nameof(ListerHaulables.ListerHaulablesTick)), prefix: new HarmonyMethod(typeof(ListerHaulablesThreaded), nameof(ListerHaulablesTick_Disabled)) {priority = PatchPriority});
            h.Patch(AccessTools.Method(typeof(ListerHaulables), nameof(ListerHaulables.ThingsPotentiallyNeedingHauling)), prefix: new HarmonyMethod(typeof(ListerHaulablesThreaded), nameof(ThingsPotentiallyNeedingHauling)) {priority = PatchPriority});
            h.Patch(AccessTools.Method(typeof(ListerHaulables), nameof(ListerHaulables.Check)), prefix: new HarmonyMethod(typeof(ListerHaulablesThreaded), nameof(Check)) {priority = PatchPriority});
            h.Patch(AccessTools.Method(typeof(ListerHaulables), nameof(ListerHaulables.CheckAdd)), prefix: new HarmonyMethod(typeof(ListerHaulablesThreaded), nameof(CheckAdd)) {priority = PatchPriority});
            h.Patch(AccessTools.Method(typeof(ListerHaulables), nameof(ListerHaulables.TryRemove)), prefix: new HarmonyMethod(typeof(ListerHaulablesThreaded), nameof(TryRemove)) {priority = PatchPriority});

            _thread = new Thread(() =>
            {
                Log.Message($"[FixDeepStorageHSK] ListerHaulablesTick thread started");
                while (true)
                {
                    try
                    {
                        var maps = Find.Maps;
                        if (maps != null)
                            foreach (var map in maps)
                                map.listerHaulables.ListerHaulablesTick_New();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[FixDeepStorageHSK] exception in thread: {e}");
                    }
                    Thread.Sleep(ThreadUpdateInterval);
                }
            });
            _thread.Start();
        }

        // patch: disable original function
        private static bool ListerHaulablesTick_Disabled() => false;

        // internal check without lock object
        private static void Check_Internal(ListerHaulables lister, Thing t)
        {
            if (lister.ShouldBeHaulable(t))
            {
                if (!lister.haulables.Contains(t))
                {
                    lister.haulables.Add(t);
                }
            }
            else if (lister.haulables.Contains(t))
            {
                lister.haulables.Remove(t);
            }
        }

        // patch: ListerHaulablesTick replacement in thread
        private static void ListerHaulablesTick_New(this ListerHaulables lister)
        {
            ListerHaulables.groupCycleIndex++;

            if (ListerHaulables.groupCycleIndex >= int.MaxValue)
                ListerHaulables.groupCycleIndex = 0;

            var allGroupsListForReading = lister.map.haulDestinationManager.AllGroupsListForReading;
            if (allGroupsListForReading.Count == 0)
                return;

            var index = ListerHaulables.groupCycleIndex % allGroupsListForReading.Count;
            var slotGroup = allGroupsListForReading[ListerHaulables.groupCycleIndex % allGroupsListForReading.Count];
            if (slotGroup.CellsList.Count != 0)
            {
                while (lister.cellCycleIndices.Count <= index)
                    lister.cellCycleIndices.Add(0);
                if (lister.cellCycleIndices[index] >= int.MaxValue)
                    lister.cellCycleIndices[index] = 0;

                for (var i = 0; i < 4; i++)
                {
                    var list = lister.cellCycleIndices;
                    list[index]++;
                    var thingList = slotGroup.CellsList[lister.cellCycleIndices[index] % slotGroup.CellsList.Count].GetThingList(lister.map)
                        .ToList(); // Make copy of the list. Prevent System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
                    foreach (var thing in thingList)
                    {
                        if (thing.def.EverHaulable)
                        {
                            // this.Check(thingList[j]);
                            Check_Internal(lister, thing);
                            /* LWM.DeepStorage remove this break in transpiler */
                            // break;
                        }
                    }
                }
            }
        }

        private static bool ThingsPotentiallyNeedingHauling(ListerHaulables __instance, ref List<Thing> __result)
        {
            lock (Locker)
            {
                __result = new List<Thing>(__instance.haulables);
                //__result = __instance.haulables;
            }
            return false;
        }

        private static bool Check(ListerHaulables __instance, Thing t)
        {
            lock (Locker)
            {
                Check_Internal(__instance, t);
            }
            return false;
        }
	
        private static bool CheckAdd(ListerHaulables __instance, Thing t)
        {
            lock (Locker)
            {
                if (__instance.ShouldBeHaulable(t) && !__instance.haulables.Contains(t))
                {
                    __instance.haulables.Add(t);
                }
            }

            return false;
        }

        private static bool TryRemove(ListerHaulables __instance, Thing t)
        {
            lock (Locker)
            {
                if (t.def.category == ThingCategory.Item && __instance.haulables.Contains(t))
                {
                    __instance.haulables.Remove(t);
                }
            }
            return false;
        }
    }
}
