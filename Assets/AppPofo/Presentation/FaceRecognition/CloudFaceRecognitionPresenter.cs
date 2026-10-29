using System;
using System.Collections.Generic;
using AppPofo.Presentation.Components;
using AppPofo.Presentation.FaceRegistration;
using AppPofo.Presentation.FaceRegistration.Detection;
using AppPofo.Presentation.Screens;
using UnityEngine;

namespace AppPofo.Presentation.FaceRecognition
{
    // Independent UUID presentation; existing SQLite long-ID cards remain untouched.
    internal sealed class CloudRecognitionHit
    {
        public CloudFacePublicCard Card;
        public Rect FaceBounds;
    }

    internal sealed class CloudFaceRecognitionPresenter
    {
        private const float MaxTrackingGapSeconds = 20f;
        private const float CardLifetimeSeconds = 13f;
        private const float NoMatchGraceSeconds = 1.5f;
        private const float MaxNoMatchGraceSeconds = 2f;
        private readonly RectTransform root;
        private readonly RectTransform overlayPrefab;
        private readonly BusinessCardPanelController cards;
        private readonly CameraPreviewOrientationEffect orientation;
        private readonly Dictionary<string, Tracked> tracked = new Dictionary<string, Tracked>();
        private readonly Vector3[] corners = new Vector3[4];
        private bool layoutDirty = true;
        private Rect lastArea;
        private bool renderProbeSubscribed;
        private bool scanInFlight;

        private sealed class Tracked
        {
            public Rect NormalizedBounds;
            public float LastObserved;
            public float NoMatchSince = -1f;
            public RectTransform Overlay;
            public BusinessCardView Card;
            public double CardCreatedAt;
            public bool AwaitingPresentation;
        }

        internal CloudFaceRecognitionPresenter(
            RectTransform root, RectTransform overlayPrefab,
            BusinessCardPanelController cards, CameraPreviewOrientationEffect orientation)
        {
            this.root = root;
            this.overlayPrefab = overlayPrefab;
            this.cards = cards;
            this.orientation = orientation;
        }

        // Display each validated server MATCH immediately. Position tracking only
        // controls card reuse; it does not require another recognition result.
        internal void Observe(IReadOnlyList<CloudRecognitionHit> matches,
            int width, int height, float now)
        {
            if (root == null || overlayPrefab == null || cards == null ||
                width <= 0 || height <= 0 || matches == null)
            {
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.PresenterInvalid);
                Clear();
                return;
            }

            var seen = new HashSet<string>();
            foreach (var match in matches)
            {
                if (match == null || match.Card == null ||
                    !Guid.TryParse(match.Card.id, out _) ||
                    string.IsNullOrWhiteSpace(match.Card.display_name) ||
                    !seen.Add(match.Card.id)) continue;

                Rect face = match.FaceBounds;
                var normalized = new Rect(
                    face.x / width,
                    1f - (face.y + face.height) / height,
                    face.width / width,
                    face.height / height);
                if (normalized.width <= 0f || normalized.height <= 0f ||
                    orientation == null ||
                    !CameraPreviewOrientationEffect.TryMapUprightBounds(
                        normalized, orientation.VisibleUprightUVRect, out Rect mapped))
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.BoundsRejected);
                    continue;
                }

                // A newly verified identity at this face position replaces the old card.
                var replaced = new List<string>();
                foreach (var other in tracked)
                    if (other.Key != match.Card.id && RectIoU(other.Value.NormalizedBounds, mapped) >= .2f)
                        replaced.Add(other.Key);
                foreach (string id in replaced) Remove(id);
                bool sameTrack = tracked.TryGetValue(match.Card.id, out Tracked entry) &&
                    now - entry.LastObserved <= MaxTrackingGapSeconds &&
                    RectIoU(entry.NormalizedBounds, mapped) >= .2f;
                if (!sameTrack)
                {
                    if (entry != null)
                    {
                        CloudFaceScanDiagnostics.Record(CloudFaceScanStage.TrackReset);
                        Remove(match.Card.id);
                    }
                    entry = new Tracked();
                    tracked[match.Card.id] = entry;
                }
                entry.LastObserved = now;
                entry.NoMatchSince = -1f;
                entry.NormalizedBounds = mapped;
                layoutDirty = true;

                if (entry.Overlay == null)
                {
                    entry.Overlay = UnityEngine.Object.Instantiate(overlayPrefab, root);
                    var view = entry.Overlay.GetComponent<FaceRecognitionOverlayView>();
                    if (view != null) view.Bind(0, null);
                }
                entry.Overlay.gameObject.SetActive(true);
                entry.Overlay.anchorMin = mapped.min;
                entry.Overlay.anchorMax = mapped.max;
                entry.Overlay.offsetMin = Vector2.zero;
                entry.Overlay.offsetMax = Vector2.zero;

                if (entry.Card == null)
                {
                    double createStartedAt = CloudFaceScanDiagnostics.ClockSeconds;
                    entry.Card = cards.CreateCloudRecognitionCard(
                        match.Card.id, match.Card.display_name, match.Card.headline, root,
                        match.Card.avatar_base64, match.Card.business_card_theme,
                        () => cards.ShowRecognizedCloudProfile(match.Card.id));
                    entry.CardCreatedAt = CloudFaceScanDiagnostics.ClockSeconds;
                    entry.AwaitingPresentation = entry.Card != null && CloudFaceScanDiagnostics.Enabled;
                    CloudFaceScanDiagnostics.Record(entry.Card != null ?
                        CloudFaceScanStage.CardCreated : CloudFaceScanStage.CardFailed,
                        durationMs: (entry.CardCreatedAt - createStartedAt) * 1000d);
                }
                else
                    entry.Card.BindCloudPublic(match.Card.display_name, match.Card.headline, match.Card.avatar_base64);
            }
            // Keep independently tracked cards positioned and expire old observations.
            Tick(now);
        }

        internal void SetScanInFlight(bool value) => scanInFlight = value;

        // A valid NO_MATCH may retain only the card at the queried face position.
        // Repeated failures never restart this deadline, and never create a card.
        internal void ObserveNoMatch(Rect face, int width, int height, float now)
        {
            if (width <= 0 || height <= 0 || orientation == null) { Clear(); return; }
            var normalized = new Rect(face.x / width, 1f - face.yMax / height,
                face.width / width, face.height / height);
            if (normalized.width <= 0f || normalized.height <= 0f ||
                !CameraPreviewOrientationEffect.TryMapUprightBounds(normalized,
                    orientation.VisibleUprightUVRect, out Rect mapped)) { Clear(); return; }
            Tracked selected = null;
            float bestOverlap = .2f;
            foreach (var item in tracked)
            {
                float overlap = RectIoU(item.Value.NormalizedBounds, mapped);
                if (overlap < bestOverlap) continue;
                bestOverlap = overlap;
                selected = item.Value;
            }
            if (selected != null && selected.NoMatchSince < 0f) selected.NoMatchSince = now;
            Tick(now);
        }

        // Remove each missing track even when another face is still in view.
        // This only removes state; a detection can never create or confirm identity.
        internal void ObserveDetections(IReadOnlyList<FaceDetectionItem> faces, int width, int height)
        {
            if (faces == null || width <= 0 || height <= 0 || orientation == null) { Clear(); return; }
            var missing = new List<string>();
            foreach (var track in tracked)
            {
                bool found = false;
                foreach (var face in faces)
                {
                    Rect bounds = face.Bounds;
                    var normalized = new Rect(bounds.x / width, 1f - bounds.yMax / height,
                        bounds.width / width, bounds.height / height);
                    if (CameraPreviewOrientationEffect.TryMapUprightBounds(normalized,
                        orientation.VisibleUprightUVRect, out var mapped) &&
                        RectIoU(track.Value.NormalizedBounds, mapped) >= .2f)
                    {
                        found = true;
                        track.Value.NormalizedBounds = mapped;
                        if (track.Value.Overlay != null)
                        {
                            track.Value.Overlay.anchorMin = mapped.min;
                            track.Value.Overlay.anchorMax = mapped.max;
                            layoutDirty = true;
                        }
                        break;
                    }
                }
                if (!found) missing.Add(track.Key);
            }
            foreach (string id in missing) Remove(id);
        }

        internal void Tick(float now)
        {
            if (tracked.Count == 0) return;
            var expired = new List<string>();
            foreach (var item in tracked)
            {
                float failedFor = item.Value.NoMatchSince < 0f ? -1f : now - item.Value.NoMatchSince;
                if (now - item.Value.LastObserved > CardLifetimeSeconds ||
                    failedFor >= MaxNoMatchGraceSeconds ||
                    (failedFor >= NoMatchGraceSeconds && !scanInFlight))
                    expired.Add(item.Key);
            }
            foreach (string key in expired) Remove(key);

            if (!layoutDirty && lastArea == root.rect) return;
            var visible = new List<Tracked>();
            var faceRects = new List<Rect>();
            foreach (var item in tracked)
            {
                Tracked entry = item.Value;
                if (entry.Overlay == null || entry.Card == null) continue;
                entry.Overlay.GetWorldCorners(corners);
                Vector2 lower = root.InverseTransformPoint(corners[0]);
                Vector2 upper = root.InverseTransformPoint(corners[2]);
                visible.Add(entry);
                faceRects.Add(Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y));
            }
            if (visible.Count == 0) return;
            lastArea = root.rect;
            layoutDirty = false;
            var slots = FaceRecognitionCamera.CalculateBusinessCardBounds(root.rect, faceRects);
            for (int i = 0; i < visible.Count; i++)
            {
                var card = visible[i].Card;
                if (card == null) continue;
                if (i >= slots.Count)
                {
                    CloudFaceScanDiagnostics.Record(CloudFaceScanStage.LayoutUnavailable);
                    card.gameObject.SetActive(false);
                    continue;
                }
                RectTransform transform = (RectTransform)card.transform;
                transform.anchorMin = transform.anchorMax = root.pivot;
                transform.pivot = new Vector2(.5f, .5f);
                transform.sizeDelta = new Vector2(900f, 360f);
                transform.localScale = Vector3.one * (slots[i].width / 900f);
                transform.anchoredPosition = slots[i].center;
                transform.SetAsLastSibling();
                card.gameObject.SetActive(true);
                CloudFaceScanDiagnostics.Record(card.gameObject.activeInHierarchy ?
                    CloudFaceScanStage.CardVisible : CloudFaceScanStage.CardInactive,
                    durationMs: (CloudFaceScanDiagnostics.ClockSeconds - visible[i].CardCreatedAt) * 1000d);
                if (visible[i].AwaitingPresentation && !renderProbeSubscribed)
                {
                    Canvas.willRenderCanvases += RecordPresentation;
                    renderProbeSubscribed = true;
                }
            }
        }

        // This is a Canvas render opportunity, not proof of display-panel scanout.
        // Observe only; never force layout, rendering, activation or recognition.
        private void RecordPresentation()
        {
            Canvas.willRenderCanvases -= RecordPresentation;
            renderProbeSubscribed = false;
            foreach (Tracked entry in tracked.Values)
            {
                if (!entry.AwaitingPresentation || entry.Card == null ||
                    !entry.Card.gameObject.activeInHierarchy) continue;
                Canvas canvas = entry.Card.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                entry.AwaitingPresentation = false;
                CloudFaceScanDiagnostics.Record(CloudFaceScanStage.CardPresented,
                    durationMs: (CloudFaceScanDiagnostics.ClockSeconds - entry.CardCreatedAt) * 1000d);
            }
        }

        private static float RectIoU(Rect a, Rect b)
        {
            float x = Mathf.Max(0f, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
            float y = Mathf.Max(0f, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            float intersection = x * y;
            float area = a.width * a.height + b.width * b.height - intersection;
            return area > 0f ? intersection / area : 0f;
        }

        private void Remove(string id)
        {
            if (!tracked.TryGetValue(id, out Tracked entry)) return;
            if (entry.Overlay != null)
            {
                entry.Overlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(entry.Overlay.gameObject);
            }
            if (entry.Card != null)
            {
                entry.Card.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(entry.Card.gameObject);
            }
            tracked.Remove(id);
            layoutDirty = true;
        }

        internal void Clear()
        {
            scanInFlight = false;
            Canvas.willRenderCanvases -= RecordPresentation;
            renderProbeSubscribed = false;
            if (tracked.Count > 0) CloudFaceScanDiagnostics.Record(CloudFaceScanStage.Cleared);
            var ids = new List<string>(tracked.Keys);
            foreach (var id in ids) Remove(id);
        }
    }
}
