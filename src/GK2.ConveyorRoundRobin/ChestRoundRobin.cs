using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GK2.ConveyorRoundRobin
{
    /// <summary>
    /// Vanilla conveyor chests never choose an output: every belt pulls for itself, in the same
    /// traversal order each tick, so with scarce supply one branch takes everything.
    ///
    /// For chests with two or more output belts this refuses those mid-tick pulls and instead
    /// hands items out once the tick's belt movement is finished (so "is this belt full" is final),
    /// starting each time from the side after the one served last.
    /// </summary>
    internal static class ChestRoundRobin
    {
        private const int MinOutputs = 2;

        // Runtime-only, keyed by the chest so nothing is ever added to the save.
        private sealed class ChestState
        {
            public int NextSlot;
            public bool Announced;
        }

        private static readonly ConditionalWeakTable<ConveyorChestComponent, ChestState> States =
            new ConditionalWeakTable<ConveyorChestComponent, ChestState>();

        private static readonly FieldInfo OnUpdatedField = AccessTools.Field(typeof(ConveyorSystem), "OnUpdated");
        private static readonly Action HandOutHandler = HandOutAll;

        private static ConveyorSystem hookedSystem;
        private static bool disabled;

        // Set only while the hand-out pass is asking a chest on behalf of one belt.
        private static ConveyorChestComponent activeChest;
        private static ConveyorComponent activeReceiver;
        private static string handedOutItemId;

        private static bool Active => hookedSystem != null && !disabled && Plugin.Enabled.Value;

        [HarmonyPatch(typeof(ConveyorSystem), nameof(ConveyorSystem.CustomUpdate))]
        [HarmonyPrefix]
        private static void EnsureHooked(ConveyorSystem __instance)
        {
            if (disabled || ReferenceEquals(hookedSystem, __instance))
            {
                return;
            }
            if (OnUpdatedField == null)
            {
                Disable("ConveyorSystem.OnUpdated not found", null);
                return;
            }
            // The belt animators read InItem/OutItem in their own OnUpdated handlers, so the
            // hand-out has to run before them: put our handler at the front of the list.
            Delegate others = Delegate.Remove((Delegate)OnUpdatedField.GetValue(__instance), HandOutHandler);
            OnUpdatedField.SetValue(__instance, Delegate.Combine(HandOutHandler, others));
            hookedSystem = __instance;
            Plugin.Log.LogInfo("Hooked conveyor system tick");
        }

        [HarmonyPatch(typeof(ConveyorChestComponent), nameof(ConveyorChestComponent.CanGiveItem))]
        [HarmonyPostfix]
        private static void GateCanGiveItem(ConveyorChestComponent __instance, ConveyorComponent conveyorComponent, ref bool __result)
        {
            if (!__result || !Active)
            {
                return;
            }
            if (ReferenceEquals(activeChest, __instance) && ReferenceEquals(activeReceiver, conveyorComponent))
            {
                return;
            }
            if (CountOutputs(__instance) >= MinOutputs)
            {
                __result = false;
            }
        }

        [HarmonyPatch(typeof(ConveyorChestComponent), nameof(ConveyorChestComponent.GiveItem))]
        [HarmonyPostfix]
        private static void RecordGiveItem(ConveyorChestComponent __instance, List<Item> __result)
        {
            if (ReferenceEquals(activeChest, __instance) && __result != null && __result.Count > 0)
            {
                handedOutItemId = __result[0].id;
            }
        }

        private static void HandOutAll()
        {
            if (!Active)
            {
                return;
            }
            try
            {
                List<ConveyorComponent> components = MainGame.Instance.GameSave.conveyorSystemData.conveyorComponents;
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i] is ConveyorChestComponent chest)
                    {
                        HandOut(chest);
                    }
                }
            }
            catch (Exception e)
            {
                // Must not break the game's own OnUpdated subscribers; fall back to vanilla.
                Disable("hand-out pass failed", e);
            }
            finally
            {
                activeChest = null;
                activeReceiver = null;
            }
        }

        private static void HandOut(ConveyorChestComponent chest)
        {
            if (chest.WgoData == null || CountOutputs(chest) < MinOutputs)
            {
                return;
            }
            List<ConveyorChestSlotData> slots = chest.SlotsData;
            ChestState state = States.GetOrCreateValue(chest);
            if (!state.Announced)
            {
                state.Announced = true;
                if (Plugin.LogHandOuts.Value)
                {
                    Plugin.Log.LogInfo($"Managing {Describe(chest)}, outputs: {DescribeOutputs(chest)}");
                }
            }
            if (chest.WgoData.Inventory.Data.Inventory.Count == 0)
            {
                return;
            }
            int start = state.NextSlot % slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                int index = (start + i) % slots.Count;
                ConveyorChestSlotData slot = slots[index];
                if (!IsOutput(chest, slot))
                {
                    continue;
                }
                ConveyorComponent receiver = slot.ConveyorWgoData?.ConveyorComponent;
                if (receiver == null)
                {
                    continue;
                }
                activeChest = chest;
                activeReceiver = receiver;
                handedOutItemId = null;
                // The belt's own vanilla pull: it only takes an item if it is empty, has not
                // moved one already this tick, and the chest has something for this side.
                receiver.PerformItemTransfer();
                activeChest = null;
                activeReceiver = null;
                if (handedOutItemId != null)
                {
                    state.NextSlot = (index + 1) % slots.Count;
                    if (Plugin.LogHandOuts.Value)
                    {
                        Plugin.Log.LogInfo($"{Describe(chest)} -> {slot.slotPosDirection}: {handedOutItemId}");
                    }
                }
            }
        }

        // A side is an output when a belt is attached there and that belt is not feeding the chest.
        private static bool IsOutput(ConveyorChestComponent chest, ConveyorChestSlotData slot)
        {
            return !SGuid.IsNullOrEmpty(slot.conveyorWgoDataUniqueId) && !chest.ParentsData.ContainsKey(slot.conveyorWgoDataUniqueId);
        }

        private static int CountOutputs(ConveyorChestComponent chest)
        {
            List<ConveyorChestSlotData> slots = chest.SlotsData;
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (IsOutput(chest, slots[i]))
                {
                    count++;
                }
            }
            return count;
        }

        private static void Disable(string reason, Exception e)
        {
            disabled = true;
            Plugin.Log.LogError($"Round-robin disabled, chests behave as in vanilla: {reason}" + (e != null ? Environment.NewLine + e : ""));
        }

        private static string Describe(ConveyorChestComponent chest)
        {
            string guid = chest.WgoData.UniqueId?.ToString() ?? "";
            return $"{chest.WgoData.id} [{(guid.Length > 8 ? guid.Substring(0, 8) : guid)}]";
        }

        private static string DescribeOutputs(ConveyorChestComponent chest)
        {
            List<string> names = new List<string>();
            foreach (ConveyorChestSlotData slot in chest.SlotsData)
            {
                if (IsOutput(chest, slot))
                {
                    names.Add(slot.slotPosDirection.ToString());
                }
            }
            return string.Join(", ", names);
        }
    }
}
