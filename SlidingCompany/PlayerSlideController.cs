using System.Reflection;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlidingCompany
{
    public class PlayerSlideController: MonoBehaviour {
        public PlayerControllerB playerController = null;
        public float slideSpeed = 0f;
        public float initialSlideSpeedBoost = 15.0f;
        public float slideFriction = 0.1f;
        public float friction = 0.97f;
        public float airFriction = 0.99f;
        const float gravity = 50f;
        const float initialSlideStaminaCost = 0.08f;
        const float carriedItemWeightMultiplier = 2.0f;
        // const float slideStaminaDrain = 0.0015f;
        const float slideStaminaDrain = 0.0f;
        // The smallest slide speed allowed before we stop the slide
        const float stopSlideSpeed = 0.5f;
        bool isSliding = false;
        bool isCrouching = false;
        PhysicMaterial originalMaterial = null;
        PhysicMaterial slideMaterial = null;
        Vector3 lastSlideDirection = Vector3.zero;

        void Start () {
            originalMaterial = playerController.thisController.material;
            slideMaterial = new PhysicMaterial("PlayerSlideMaterial");
            slideMaterial.staticFriction = slideFriction;
            slideMaterial.dynamicFriction = slideFriction;
        }

        void OnSlideStart() {
            isSliding = true;
            playerController.playerBodyAnimator.SetBool("Walking", false);
            playerController.playerBodyAnimator.SetBool("Sprinting", false);
            playerController.playerBodyAnimator.SetBool("Jumping", false);
            playerController.playerBodyAnimator.SetBool("crouching", true);

            // TODO: Fix animation issue where we sometimes are standing while slanding
        }

        void SlideUpdate(Vector3 slideDirection) {
            // Ensure that the correct animations are playing for a slide
            playerController.playerBodyAnimator.SetBool("Walking", false);
            playerController.playerBodyAnimator.SetBool("Sprinting", false);
            playerController.playerBodyAnimator.SetBool("Jumping", false);
            playerController.playerBodyAnimator.SetBool("crouching", true);

            // Slide in some direction
            IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Disable();
            playerController.thisController.material = slideMaterial;
            playerController.thisController.Move(slideDirection * slideSpeed * Time.fixedDeltaTime);

            // Consume some stamina while sliding
            playerController.sprintMeter = Mathf.Clamp(playerController.sprintMeter - slideStaminaDrain, 0f, 1f);
        }

        void OnSlideEnd() {
            isSliding = false;

            // Reset the material to the original
            playerController.thisController.material = originalMaterial;

            // Enable sprinting now that our slide has basically ended
            IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint", false).Enable();
            FixAnimations();
        }

        void FixAnimations() {
            if (!isSliding) {
                // Since animations can get a little jank, we will run this every update to ensure everything is fine
                bool isJumping = (bool)typeof(PlayerControllerB).GetField("isJumping", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(playerController);
                bool isWalking = (bool)typeof(PlayerControllerB).GetField("isWalking", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(playerController);

                // Ensure our animations are all set correctly
                playerController.playerBodyAnimator.SetBool("Jumping", isJumping);
                playerController.playerBodyAnimator.SetBool("Walking", isWalking);
                playerController.playerBodyAnimator.SetBool("Sprinting", playerController.isSprinting);
                playerController.playerBodyAnimator.SetBool("crouching", playerController.isCrouching);
            }
            else {
                // Otherwise we are busy sliding
                playerController.playerBodyAnimator.SetBool("Walking", false);
                playerController.playerBodyAnimator.SetBool("Sprinting", false);
                playerController.playerBodyAnimator.SetBool("Jumping", false);
                playerController.playerBodyAnimator.SetBool("crouching", true);
            }
        }

        void Update() {
            FixAnimations();
        }

        float getWeightMultiplier() {
            float additionalWeight = playerController.carryWeight - 1.0f;
            return 1.0f + additionalWeight * carriedItemWeightMultiplier;
        }

        float getFriction() {
            if (playerController.thisController.isGrounded) {
                return friction;
            }
            return airFriction;
        }

        void FixedUpdate() {
            bool isJumping = (bool) typeof(PlayerControllerB).GetField("isJumping", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(playerController);
            bool wasSliding = isSliding;

            if (!isCrouching) {
                // If we aren't already crouching
                isCrouching = playerController.isCrouching;

                // If we were already moving in some direction beforehand, we can start sliding if not exhausted
                isSliding = !playerController.isExhausted && playerController.sprintMeter >= initialSlideStaminaCost * getWeightMultiplier() && isCrouching && playerController.thisController.velocity.magnitude > 0;
                if (isSliding && playerController.thisController.isGrounded) {
                    // Carry over any existing velocity into the slide
                    slideSpeed = playerController.thisController.velocity.magnitude + initialSlideSpeedBoost / getWeightMultiplier();

                    // Let's also use a bit of stamina since we gave a small speed boost
                    playerController.sprintMeter = Mathf.Clamp(playerController.sprintMeter - initialSlideStaminaCost * getWeightMultiplier(), 0f, 1f);
                }
                else if (isSliding) {
                    // If we weren't grounded, we can queue a slide for when we land by setting isCrouching to false
                    isCrouching = false;
                }
            }
            else {
                if (!playerController.isCrouching) {
                    // We were previously crouched, but now we aren't
                    isCrouching = false;
                    isSliding = false;
                }
            }

            if (wasSliding && !isSliding) {
                OnSlideEnd();
                return;
            }
            if (!wasSliding && isSliding) {
                OnSlideStart();
            }

            // Apply some friction
            slideSpeed *= getFriction();
            if (Mathf.Abs(slideSpeed) <= stopSlideSpeed) {
                slideSpeed = 0f;
            }

            if (isJumping || !playerController.thisController.isGrounded) {
                // If we are jumping or not grounded, keep applying slide velocity
                // otherwise we have an abrupt loss of velocity when slide jumping
                playerController.thisController.Move(lastSlideDirection * slideSpeed * Time.fixedDeltaTime);
            }

            if (!isSliding && !isCrouching) {
                this.OnSlideEnd();
                return;
            }

            RaycastHit hit;
            Ray floorRay = new Ray(playerController.gameplayCamera.transform.position, Vector3.down);
            Vector3 slideDirection = playerController.gameplayCamera.transform.forward;

            // If we are sliding while on a slope, let's slide in the correct direction
            if (playerController.thisController.isGrounded && isSliding) {
                Physics.Raycast(floorRay, out hit, 20.0f, playerController.playersManager.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore);
                slideDirection = Vector3.ProjectOnPlane(slideDirection, hit.normal).normalized;
                lastSlideDirection = new Vector3(slideDirection.x, slideDirection.y, slideDirection.z);
                // Add speed based on the steepness of the slope
                float steepness = -Vector3.Dot(slideDirection, Vector3.up);
                slideSpeed += steepness * gravity * playerController.carryWeight * Time.fixedDeltaTime;
                if (Mathf.Abs(slideSpeed) <= stopSlideSpeed) {
                    slideSpeed = 0f;
                }
            }

            if (isSliding && Mathf.Abs(slideSpeed) >= stopSlideSpeed) {
                this.SlideUpdate(slideDirection);
            }
            else {
                this.OnSlideEnd();
            }
        }
    }
}
