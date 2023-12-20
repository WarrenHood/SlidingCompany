using System.Reflection;
using GameNetcodeStuff;
using UnityEngine;

namespace SlidingCompany
{
    public class PlayerSlideController: MonoBehaviour {
        public PlayerControllerB playerController = null;
        public float slideSpeed = 0f;
        public float initialSlideSpeedBoost = 15.0f;
        public float slideFriction = 0.1f;
        public float friction = 0.97f;
        const float gravity = 150.0f;
        const float initialSlideStaminaCost = 0.08f;
        const float slideStaminaDrain = 0.002f;
        bool isSliding = false;
        bool isCrouching = false;
        PhysicMaterial originalMaterial = null;
        PhysicMaterial slideMaterial = null;

        void Start () {
            originalMaterial = playerController.thisController.material;
            slideMaterial = new PhysicMaterial("PlayerSlideMaterial");
            slideMaterial.staticFriction = slideFriction;
            slideMaterial.dynamicFriction = slideFriction;
        }

        void FixedUpdate() {
            bool isJumping = (bool) typeof(PlayerControllerB).GetField("isJumping", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(playerController);

            if (playerController.isCrouching) {
                // Ensure we are in our crouching animation, even after crouching mid-air
                playerController.playerBodyAnimator.SetBool("crouching", true);
            }
            else {
                playerController.playerBodyAnimator.SetBool("crouching", false);
            }

            if (isJumping) {
                playerController.playerBodyAnimator.SetBool("Jumping", true);
            }
            else {
                playerController.playerBodyAnimator.SetBool("Jumping", false);
            }

            if (!isCrouching) {
                // If we aren't already crouching
                isCrouching = playerController.isCrouching;

                // If we were already moving in some direction beforehand, we can start sliding if not exhausted
                isSliding = !playerController.isExhausted && playerController.sprintMeter >= initialSlideStaminaCost && isCrouching && playerController.thisController.velocity.magnitude > 0;
                if (isSliding && playerController.thisController.isGrounded) {
                    // Carry over any existing velocity into the slide
                    slideSpeed = playerController.thisController.velocity.magnitude + initialSlideSpeedBoost;

                    // Let's also use a bit of stamina since we gave a small speed boost
                    playerController.sprintMeter = Mathf.Clamp(playerController.sprintMeter - initialSlideStaminaCost, 0f, 1f);

                    // TODO: Begin playing slide audio
                }
                else if (isSliding) {
                    // If we weren't grounded, we can queue a slide for when we land by setting isCrouching to false
                    isCrouching = false;
                }
            }
            else {
                // If we aren't crouching already
                if (!playerController.isCrouching) {
                    isCrouching = false;
                    isSliding = false;
                }
            }

            // Apply some friction
            slideSpeed *= friction;
            if (slideSpeed < 0.1) {
                slideSpeed = 0f;
                // TODO: Stop playing sliding audio if it is playing
            }

            if (isJumping || !playerController.thisController.isGrounded) {
                // If we are jumping or not grounded, keep applying slide velocity
                // otherwise we have an abrupt loss of velocity when slide jumping
                playerController.thisController.Move(playerController.thisPlayerBody.transform.forward * slideSpeed * Time.fixedDeltaTime);
            }

            if (!isSliding && !isCrouching) {
                IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Enable();
                // TODO: Stop playing sliding audio

                if (playerController.thisController.material != originalMaterial) {
                    // Reset the material to the original
                    playerController.thisController.material = originalMaterial;
                }
                return;
            }

            RaycastHit hit;
            Ray floorRay = new Ray(playerController.gameplayCamera.transform.position, Vector3.down);
            Vector3 slideDirection = playerController.gameplayCamera.transform.forward;

            // If we are crouching on a slope, start sliding
            if (playerController.thisController.isGrounded) {
                Physics.Raycast(floorRay, out hit, 20.0f, playerController.playersManager.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore);
                if (!isSliding) {
                    // Carry over any existing velocity into the slide
                    slideSpeed = playerController.thisController.velocity.magnitude;
                    isSliding = true;
                    // TODO: Begin playing slide audio
                }
                slideDirection = Vector3.ProjectOnPlane(slideDirection, hit.normal).normalized;

                // Steepness of the slope
                float steepness = -Vector3.Dot(slideDirection, Vector3.up);
                slideSpeed += steepness * gravity * Time.fixedDeltaTime;
                if (slideSpeed < 0.1) {
                    slideSpeed = 0f;
                    // TODO: Stop playing sliding audio
                }
            }
            else {
                isSliding = false;
            }


            // Setup slide materials
            if (isSliding && slideSpeed > 0f) {
                // Slide in some direction
                IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Disable();
                playerController.thisController.material = slideMaterial;
                playerController.thisController.Move(slideDirection * slideSpeed * Time.fixedDeltaTime);

                // Consume some stamina while sliding
                playerController.sprintMeter = Mathf.Clamp(playerController.sprintMeter - slideStaminaDrain, 0f, 1f);
            }
            else {
                if (playerController.thisController.material != originalMaterial) {
                    // Reset the material to the original
                    playerController.thisController.material = originalMaterial;

                    // Enable sprinting now that our slide has basically ended
                    IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Enable();
                }
            }
        }
    }
}
