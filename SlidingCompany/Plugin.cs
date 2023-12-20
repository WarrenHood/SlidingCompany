using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using GameNetcodeStuff;
using System.Reflection;
using System.Collections;

namespace SlidingCompany
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        [HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB), "Start")]
        [HarmonyPostfix]
        static void AttachPlayerSlideScript(GameNetcodeStuff.PlayerControllerB __instance) {
            // Attaches the PlayerSlideController component to this gameobject
            PlayerSlideController slideController = __instance.thisPlayerBody.gameObject.AddComponent<PlayerSlideController>();
            slideController.playerController = __instance;
        }

        [HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB), "Crouch_performed")]
        [HarmonyPrefix]
        static bool AllowCrouchWhileJumping(GameNetcodeStuff.PlayerControllerB __instance, InputAction.CallbackContext context) {
            if (!context.performed) {
                return false;
            }
            if (__instance.quickMenuManager.isMenuOpen) {
                return false;
            }
            if ((!__instance.IsOwner || !__instance.isPlayerControlled || (__instance.IsServer && !__instance.isHostPlayerObject)) && !__instance.isTestingPlayer) {
                return false;
            }
            if (__instance.inSpecialInteractAnimation || __instance.isTypingChat) {
                return false;
            }
            __instance.Crouch(!__instance.isCrouching);
            __instance.playerBodyAnimator.SetBool("Jumping", false);

            // Disable sprinting for now
            if (__instance.isCrouching) {
                IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Disable();
            }

            // Skip the original Crouch_performed
            return false;
        }

        [HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB), "Jump_performed")]
        [HarmonyPrefix]
        static bool AllowJumpWhileCrouching(
            GameNetcodeStuff.PlayerControllerB __instance,
            InputAction.CallbackContext context,
            ref bool ___isJumping,
            ref float ___playerSlidingTimer,
            ref Coroutine ___jumpCoroutine
        ) {
            if (__instance.quickMenuManager.isMenuOpen) {
                return false;
            }
            if ((!__instance.IsOwner || !__instance.isPlayerControlled || (__instance.IsServer && !__instance.isHostPlayerObject)) && !__instance.isTestingPlayer) {
                return false;
            }
            if (__instance.inSpecialInteractAnimation) {
                return false;
            }
            if (__instance.isTypingChat) {
                return false;
            }
            if (__instance.isMovementHindered > 0 && !__instance.isUnderwater) {
                return false;
            }
            if (__instance.isExhausted) {
                return false;
            }

            // Check if player is near the ground
            Ray interactRay = new Ray(__instance.transform.position, Vector3.down);
            bool isNearGround = Physics.Raycast(interactRay, 0.15f, StartOfRound.Instance.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore);

            if ((__instance.thisController.isGrounded || (!___isJumping && isNearGround)) 
                && !___isJumping 
                && (!__instance.isPlayerSliding || ___playerSlidingTimer > 2.5f)) {
                ___playerSlidingTimer = 0f;
                ___isJumping = true;

                // Uncrouch if we are crouching
                __instance.isCrouching = false;
                __instance.playerBodyAnimator.SetBool("Crouching", false);

                __instance.sprintMeter = Mathf.Clamp(__instance.sprintMeter - 0.08f, 0f, 1f);
                __instance.movementAudio.PlayOneShot(StartOfRound.Instance.playerJumpSFX);
                if (___jumpCoroutine != null) {
                    __instance.StopCoroutine(___jumpCoroutine);
                }

                MethodInfo playerJumpMethodInfo = typeof(PlayerControllerB).GetMethod("PlayerJump", BindingFlags.NonPublic | BindingFlags.Instance);
                ___jumpCoroutine = __instance.StartCoroutine((IEnumerator)playerJumpMethodInfo.Invoke(__instance, new object[] { }));
            }
            return false;
        }
    }
}