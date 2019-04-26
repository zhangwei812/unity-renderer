using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DCL.Helpers;
using DCL.Controllers;
using DCL.Models;
using DCL.Interface;

namespace DCL.Components
{
    public class UIScrollRect : UIShape<UIScrollRectRefContainer, UIScrollRect.Model>
    {
        [System.Serializable]
        new public class Model : UIShape.Model
        {
            public float valueX = 0;
            public float valueY = 0;
            public Color borderColor;
            public Color backgroundColor = Color.clear;
            public bool isHorizontal = false;
            public bool isVertical = true;
            public float paddingTop;
            public float paddingRight;
            public float paddingBottom;
            public float paddingLeft;
            public string OnChanged;
        }

        public override string referencesContainerPrefabName => "UIScrollRect";

        public UIScrollRect(ParcelScene scene) : base(scene)
        {
        }

        public override void AttachTo(DecentralandEntity entity)
        {
            Debug.LogError("Aborted UIScrollRectShape attachment to an entity. UIShapes shouldn't be attached to entities.");
        }

        public override void DetachFrom(DecentralandEntity entity)
        {
        }

        public override void OnChildAttached(UIShape parent, UIShape childComponent)
        {
            base.OnChildAttached(parent, childComponent);
            childComponent.OnAppliedChanges -= RefreshContainerForShape;
            childComponent.OnAppliedChanges += RefreshContainerForShape;
            RefreshContainerForShape(childComponent);
        }

        public override void OnChildDetached(UIShape parent, UIShape childComponent)
        {
            base.OnChildDetached(parent, childComponent);
            childComponent.OnAppliedChanges -= RefreshContainerForShape;
        }

        void RefreshContainerForShape(BaseDisposable updatedComponent)
        {
            RefreshAll();
            referencesContainer.fitter.RefreshRecursively();
            AdjustChildHook();
            referencesContainer.scrollRect.Rebuild(CanvasUpdate.MaxUpdateValue);
        }

        void AdjustChildHook()
        {
            UIScrollRectRefContainer rc = referencesContainer;
            rc.childHookRectTransform.SetParent(rc.layoutElementRT, false);
            rc.childHookRectTransform.SetToMaxStretch();
            rc.childHookRectTransform.SetParent(rc.content, true);
            RefreshDCLLayoutRecursively(false, true);
        }

        public override void RefreshDCLLayoutRecursively(bool refreshSize = true, bool refreshAlignmentAndPosition = true)
        {
            base.RefreshDCLLayoutRecursively(refreshSize, refreshAlignmentAndPosition);
            referencesContainer.fitter.RefreshRecursively();
        }


        public override IEnumerator ApplyChanges(string newJson)
        {
            UIScrollRectRefContainer rc = referencesContainer;

            rc.contentBackground.color = model.backgroundColor;

            // Apply padding
            rc.paddingLayoutGroup.padding.bottom = Mathf.RoundToInt(model.paddingBottom);
            rc.paddingLayoutGroup.padding.top = Mathf.RoundToInt(model.paddingTop);
            rc.paddingLayoutGroup.padding.left = Mathf.RoundToInt(model.paddingLeft);
            rc.paddingLayoutGroup.padding.right = Mathf.RoundToInt(model.paddingRight);

            rc.scrollRect.horizontal = model.isHorizontal;
            rc.scrollRect.vertical = model.isVertical;

            rc.HScrollbar.value = model.valueX;
            rc.VScrollbar.value = model.valueY;

            rc.scrollRect.onValueChanged.AddListener(OnChanged);

            RefreshAll();
            referencesContainer.fitter.RefreshRecursively();
            AdjustChildHook();
            return null;
        }

        void OnChanged(Vector2 scrollingValues)
        {
            WebInterface.ReportOnScrollChange(scene.sceneData.id, model.OnChanged, scrollingValues, 0);
        }
        
        public override void Dispose()
        {
            referencesContainer.scrollRect.onValueChanged.RemoveAllListeners();
            Utils.SafeDestroy(referencesContainer.gameObject);

            base.Dispose();
        }
    }
}
