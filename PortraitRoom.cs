using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cinemachine;

namespace ChillPortrait
{
    internal sealed class PortraitRoom
    {
        private static readonly Type RoomCameraType = Type.GetType("Bulbul.RoomCameraManager, Assembly-CSharp", false);
        private static readonly FieldInfo StoryPlaying = RoomCameraType?.GetField("isPlayingStory", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ViewType = RoomCameraType?.GetField("currentVirtualCameraType", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly ConfigEntry<float> zoom;
        private readonly List<LayoutOffset> hud = new List<LayoutOffset>();
        private Camera roomCamera, adjustedCamera;
        private Quaternion appliedRotation;
        private float appliedFieldOfView;
        private CinemachineBrain brain;
        private MonoBehaviour manager;
        private Transform head;
        private Vector3 aim;
        private Quaternion originalRotation;
        private float originalFieldOfView;
        private float nextLookup;
        private bool hasAim;
        private bool enabled;

        internal PortraitRoom(ConfigEntry<float> zoom)
        {
            this.zoom = zoom;
        }

        internal void Tick(bool allowed, bool interactive)
        {
            enabled = allowed && Screen.height > Screen.width && SceneManager.GetActiveScene().name == "RoomScene";
            if (!enabled)
            {
                RestoreCamera();
                if (hud.Count != 0) RestoreHud();
                hasAim = false;
                return;
            }

            if (Time.realtimeSinceStartup >= nextLookup)
            {
                nextLookup = Time.realtimeSinceStartup + 1f;
                if (!roomCamera)
                {
                    brain = UnityEngine.Object.FindObjectOfType<CinemachineBrain>();
                    roomCamera = brain ? brain.OutputCamera : Camera.main;
                }
                if (!manager && RoomCameraType != null)
                    manager = UnityEngine.Object.FindObjectOfType(RoomCameraType) as MonoBehaviour;
                if (!head)
                {
                    foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>())
                    {
                        if (animator.name != "Character" || !animator.isHuman)
                            continue;
                        head = animator.GetBoneTransform(HumanBodyBones.Head);
                        if (head) break;
                    }
                }
                if (hud.Count == 0)
                    FindHud();
            }

            ApplyCamera(interactive);
        }

        private void ApplyCamera(bool interactive)
        {
            if (!roomCamera || !head || !IsEverydayView())
            {
                RestoreCamera();
                hasAim = false;
                return;
            }

            Quaternion pose = roomCamera.transform.rotation;
            float fieldOfView = roomCamera.fieldOfView;
            if (adjustedCamera != roomCamera || Quaternion.Angle(pose, appliedRotation) > 0.01f)
                originalRotation = pose;
            if (adjustedCamera != roomCamera || !Mathf.Approximately(fieldOfView, appliedFieldOfView))
                originalFieldOfView = fieldOfView;
            Vector3 direction = head.position - roomCamera.transform.position;
            if (direction.sqrMagnitude < 0.1f || Vector3.Dot(direction, originalRotation * Vector3.forward) < 0.15f)
            {
                RestoreCamera();
                hasAim = false;
                return;
            }

            if (interactive && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0f)
                    zoom.Value -= scroll * 2f;
            }

            Vector3 target = head.position - Vector3.up * 0.14f;
            aim = hasAim ? Vector3.Lerp(aim, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f)) : target;
            hasAim = true;
            float targetFieldOfView = Mathf.Clamp(Mathf.Max(originalFieldOfView, 42f) + zoom.Value, 26f, 78f);
            appliedFieldOfView = adjustedCamera == roomCamera
                ? Mathf.MoveTowards(appliedFieldOfView, targetFieldOfView, 90f * Time.unscaledDeltaTime)
                : targetFieldOfView;
            adjustedCamera = roomCamera;
            appliedRotation = Quaternion.LookRotation(aim - roomCamera.transform.position, Vector3.up);
            roomCamera.transform.rotation = appliedRotation;
            roomCamera.fieldOfView = appliedFieldOfView;
        }

        internal string Describe() =>
            $"Portrait enabled={enabled} camera={roomCamera?.name} head={head?.name} manager={manager?.name} " +
            $"view={(manager ? ViewType?.GetValue(manager) : null)} story={(manager ? StoryPlaying?.GetValue(manager) : null)} " +
            $"adjusted={adjustedCamera?.name}";
        private bool IsEverydayView()
        {
            if (!manager || StoryPlaying == null || ViewType == null || (bool)StoryPlaying.GetValue(manager))
                return false;
            if (brain && brain.IsBlending)
                return false;
            string view = ViewType.GetValue(manager)?.ToString();
            return view == "Initial" || view == "Base" || view == "Chair";
        }

        private void RestoreCamera()
        {
            if (adjustedCamera)
            {
                if (Quaternion.Angle(adjustedCamera.transform.rotation, appliedRotation) <= 0.01f)
                    adjustedCamera.transform.rotation = originalRotation;
                if (Mathf.Approximately(adjustedCamera.fieldOfView, appliedFieldOfView))
                    adjustedCamera.fieldOfView = originalFieldOfView;
            }
            adjustedCamera = null;
        }
        private void FindHud()
        {
            var ui = GameObject.Find("Paremt/PCPlatform/Canvas/UI");
            if (!ui)
                return;
            Transform root = ui.transform;
            Move(root, "MostFrontArea/UI_FacilityMusic", new Vector2(0f, 97f), 1f);
            Move(root, "MostFrontArea/CenterIcons", new Vector2(-124f, 0f), 1f);
            Move(root, "MostFrontArea/TopIcons", new Vector2(0f, -12f), 0.9f);
            Move(root, "UI_FacilityPomodoro/PomodoroTimer", Vector2.zero, 0.88f);
        }

        private void Move(Transform root, string path, Vector2 offset, float scale)
        {
            var rect = root.Find(path) as RectTransform;
            if (rect == null)
                return;
            hud.Add(new LayoutOffset(rect));
            rect.anchoredPosition += offset;
            rect.localScale *= scale;
        }

        private void RestoreHud()
        {
            foreach (var item in hud)
                item.Restore();
            hud.Clear();
        }

        internal void Reset()
        {
            RestoreCamera();
            RestoreHud();
            roomCamera = null;
            brain = null;
            manager = null;
            head = null;
            hasAim = false;
            nextLookup = 0;
        }

        internal static string Path(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private readonly struct LayoutOffset
        {
            private readonly RectTransform rect;
            private readonly Vector2 position;
            private readonly Vector3 scale;

            internal LayoutOffset(RectTransform rect)
            {
                this.rect = rect;
                position = rect.anchoredPosition;
                scale = rect.localScale;
            }

            internal void Restore()
            {
                if (rect)
                {
                    rect.anchoredPosition = position;
                    rect.localScale = scale;
                }
            }
        }
    }
}
