using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FixDeepStorageHSK
{
    [StaticConstructorOnStartup]
    public class FixDeepStorageHSK
    {
        static FixDeepStorageHSK()
        {
            var h = new Harmony("pirateby.hsk.deepstorage+pickupandhaul.support");

            // get lambda from MakeNewToils => releaseReservation.initAction:
            //   void PickUpAndHaul.JobDriver_UnloadYourHauledInventory/'<>c__DisplayClass4_0'::'<MakeNewToils>b__3'()
            var mnt = AccessTools.TypeByName("PickUpAndHaul.JobDriver_UnloadYourHauledInventory")?
                .GetNestedTypes(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(t => t.FullName.Contains("c__DisplayClass4_0"))?
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .First(x => x.Name.EndsWith("b__3") && x.ReturnType == typeof(void));

            if (mnt != null)
            {
                
                h.Patch(mnt, transpiler: new HarmonyMethod(typeof(FixDeepStorageHSK), nameof(Transpiler)));
                Log.Message($"[FixDeepStorageHSK] PickUpAndHaul fixed for comp with LWM.DeepStorage");
            }

            var rocketman = AccessTools.TypeByName("RocketMan.Optimizations.StatWorker_GetValueUnfinalized_Hijacked_Patch");
            if (rocketman != null)
            {
                var replacemant = AccessTools.Method(rocketman, "Replacemant");
                //var expireCache = AccessTools.Method(rocketman, "ProcessExpiryCache");
                //var updateCache = AccessTools.Method(rocketman, "UpdateCache");
                if (replacemant != null)
                {
                    h.Patch(replacemant, prefix: new HarmonyMethod(typeof(FixDeepStorageHSK), nameof(RocketMan_Replacemant)));
                    Log.Message($"[FixDeepStorageHSK] RocketMan2 fixed");
                }
            }

            ListerHaulablesThreaded.InitPatches(h);
        }

        public static bool RocketMan_Replacemant(StatWorker statWorker, StatRequest req, bool applyPostProcess, ref float __result)
        {
            if (!UnityData.IsInMainThread)
            {
				// return not cached
                __result = statWorker.GetValueUnfinalized(req, applyPostProcess);
                return false;
            }

            if (req.thingInt == null && req.stuffDefInt == null && req.defInt == null)
            {
                Log.ErrorOnce($"Try get value from Empty StatRequest!", "rocketman.patch".GetHashCode());
                __result = 0;
                return false;
            }
            return true;
        }

        private static bool RunIfMainThread() => UnityData.IsInMainThread;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var hskActive = AccessTools.Method("PickUpAndHaul.ModCompatibilityCheck:get_HCSKIsActive");
            if (hskActive == null)
            {
                Log.Error($"[FixDeepStorageHSK] PickUpAndHaul.ModCompatibilityCheck:get_HCSKIsActive not found.");
                yield break;
            }

            /*
            18	0047	call	bool PickUpAndHaul.ModCompatibilityCheck::get_HCSKIsActive()
            19	004C	brtrue.s	37 (0093) ret 
             */
            bool skipNext = false;
            foreach (var ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && ci.operand == hskActive) skipNext = true;
                else if (skipNext) skipNext = false;
                else yield return ci;
            }

            /*
            Toil releaseReservation = new Toil
            {
                initAction = () =>
                {
                    if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob)
                        // REMOVED THIS CHECK => && !ModCompatibilityCheck.HCSKIsActive
                    )
                        pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
                }
            };
             */
        }
    }
}
