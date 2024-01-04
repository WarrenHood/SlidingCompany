using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Networking;

namespace SlidingCompany
{
    public class PlayerSlideController: MonoBehaviour {
        public PlayerControllerB playerController = null;
        public float slideSpeed = 0f;
        public float initialSlideSpeedBoost = 15.0f;
        public float slideFriction = 0.1f;
        public float friction = 0.97f;
        public float airFriction = 0.99f;
        // The maximum angle change before we stop hugging the terrain
        public float maxSlideAngleChange = 30f;
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
        AudioSource slidingAudio = null;

        private static async Task<AudioClip> GetAudioClip(string filePath, AudioType fileType) {

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(filePath, fileType)) {
                var result = www.SendWebRequest();

                while (!result.isDone) { await Task.Delay(100); }

                if (www.result == UnityWebRequest.Result.ConnectionError) {
                    Debug.Log(www.error);
                    return null;
                }
                else {
                    return DownloadHandlerAudioClip.GetContent(www);
                }
            }
        }

        async void Start () {
            originalMaterial = playerController.thisController.material;
            slideMaterial = new PhysicMaterial("PlayerSlideMaterial");
            slideMaterial.staticFriction = slideFriction;
            slideMaterial.dynamicFriction = slideFriction;
            // Hardcoding undefined-SlidingCompany for now
            string slidingSoundPath = Path.Combine(Paths.PluginPath, "undefined-SlidingCompany", "SlidingCompany", "concrete.wav");
            Debug.Log("Attempting to load concrete audio source at: " + slidingSoundPath);
            AudioClip slidingSound = await GetAudioClip(slidingSoundPath, AudioType.WAV);
            if (slidingSound != null ) {
                Debug.Log("Loaded concrete audio clip.");
                Debug.Log("Loaded concrete audio data status: " + slidingSound.LoadAudioData());
                slidingAudio = playerController.thisController.gameObject.AddComponent<AudioSource>();
                if (slidingAudio != null) {
                    Debug.Log("Successfully attached an AudioSource to player gameobject");
                    slidingAudio.clip = slidingSound;
                    slidingAudio.playOnAwake = false;
                    slidingAudio.loop = true;
                }
                else {
                    Debug.Log("Failed to attach an AudioSource to player gameobject");
                }
            }
            else {
                Debug.Log("Failed to load concrete audio clip");
            }
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

        void UpdateSlideAudio() {
            if (slidingAudio != null) {
                if (isSliding) {
                    if (!slidingAudio.isPlaying) {
                        slidingAudio.Play();
                    }
                }
                else {
                    if (slidingAudio.isPlaying) {
                        slidingAudio.Stop();
                    }
                }
            }
        }

        void OnSlideEnd() {
            isSliding = false;
            if (slidingAudio != null && slidingAudio.isPlaying) {
                slidingAudio.Stop();
            }

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
        bool ShouldUpdate() {
            // This is required to stop other players vibrating/shaking
            if (
                (!playerController.IsOwner || !playerController.isPlayerControlled || (playerController.IsServer && !playerController.isHostPlayerObject))
                && !playerController.isTestingPlayer
            ) {
                return false;
            }
            return true;
        }
        void Update() {
            if (playerController.isPlayerDead) {
                isSliding = false;
                isCrouching = false;
            }
            UpdateSlideAudio();

            if (ShouldUpdate()) {
                FixAnimations();
            }
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
            if (!ShouldUpdate()) {
                return;
            }
            bool isJumping = (bool) typeof(PlayerControllerB).GetField("isJumping", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(playerController);
            bool wasSliding = isSliding;

            if (!isCrouching) {
                // If we aren't already crouching
                isCrouching = playerController.isCrouching;

                // If we were already moving in some direction beforehand, we can start sliding if not exhausted
                isSliding = !playerController.isExhausted
                    && !playerController.isPlayerDead
                    && playerController.sprintMeter >= initialSlideStaminaCost * getWeightMultiplier() 
                    && isCrouching 
                    && playerController.thisController.velocity.magnitude > 0;
                if (isSliding && playerController.thisController.isGrounded) {
                    // Carry over any existing velocity into the slide
                    slideSpeed = playerController.thisController.velocity.magnitude + initialSlideSpeedBoost / getWeightMultiplier();

                    // Let's also use a bit of stamina since we gave a small speed boost
                    playerController.sprintMeter = Mathf.Clamp(playerController.sprintMeter - initialSlideStaminaCost * getWeightMultiplier(), 0f, 1f);

                    // Set the slide direction to nothing since we just started sliding
                    lastSlideDirection = Vector3.zero;
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

            if (playerController.isPlayerDead) {
                // Dead players can't slide
                isSliding = false;
                isCrouching = false;
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

            UpdateSlideAudio();

            RaycastHit hit;
            Ray floorRay = new Ray(playerController.gameplayCamera.transform.position, Vector3.down);
            Vector3 slideDirection = playerController.gameplayCamera.transform.forward.normalized;

            // Handle slopes
            if (playerController.thisController.isGrounded && isSliding) {
                Physics.Raycast(floorRay, out hit, 20.0f, playerController.playersManager.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore);
                slideDirection = Vector3.ProjectOnPlane(slideDirection, hit.normal).normalized;
                // Add speed based on the steepness of the slope
                float steepness = -Vector3.Dot(slideDirection, Vector3.up);

                /*
                 * TODO: Finish implementing this
                // Check if the change in slide direction is greater than some threshold
                if (lastSlideDirection.magnitude > 0f) {
                    float angleChange = Vector3.Angle(lastSlideDirection, slideDirection);
                    if (angleChange > maxSlideAngleChange && slideDirection.y < lastSlideDirection.y) {
                        Debug.Log("Current angle change: " + angleChange + ", previous y=" + lastSlideDirection.y + ", current y=" + slideDirection.y);
                        // We are moving down a very steep hill, so keep our last velocity.
                        // This should allow us to take ramps
                        slideDirection = lastSlideDirection;
                        steepness = 0f;
                    }
                }*/

                slideSpeed += steepness * gravity * playerController.carryWeight * Time.fixedDeltaTime;
                if (Mathf.Abs(slideSpeed) <= stopSlideSpeed) {
                    slideSpeed = 0f;
                }
                lastSlideDirection = new Vector3(slideDirection.x, slideDirection.y, slideDirection.z);
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
