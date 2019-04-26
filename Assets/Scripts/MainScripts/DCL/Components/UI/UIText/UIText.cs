using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DCL.Helpers;
using DCL.Controllers;
using DCL.Models;
using TMPro;

namespace DCL.Components
{
    public class UIText : UIShape<UITextReferencesContainer, UIText.Model>
    {
        [System.Serializable]
        new public class Model : UIShape.Model
        {
            public TextShape.Model textModel;
        }

        public override string referencesContainerPrefabName => "UIText";

        public UIText(ParcelScene scene) : base(scene)
        {
        }

        public override void AttachTo(DecentralandEntity entity)
        {
            Debug.LogError("Aborted UITextShape attachment to an entity. UIShapes shouldn't be attached to entities.");
        }

        public override void DetachFrom(DecentralandEntity entity)
        {
        }

        public override IEnumerator ApplyChanges(string newJson)
        {
            if (!scene.isTestScene)
                model.textModel = JsonUtility.FromJson<TextShape.Model>(newJson);

            TextShape.ApplyModelChanges(referencesContainer.text, model.textModel);

            RefreshAll();
            return null;
        }

        public override void RefreshDCLLayout(bool refreshSize=true, bool refreshAlignmentAndPosition=true)
        {
            if (refreshSize)
            {
                referencesContainer.text.ForceMeshUpdate(false);
                RectTransform parentTransform = referencesContainer.GetComponentInParent<RectTransform>();
                float width, height;
                Bounds b = referencesContainer.text.textBounds;

                if (model.textModel.adaptWidth)
                    width = b.size.x;
                else
                    width = model.width.GetScaledValue(parentTransform.rect.width);

                if (model.textModel.adaptHeight)
                    height = b.size.y;
                else
                    height = model.width.GetScaledValue(parentTransform.rect.height);

                referencesContainer.layoutElementRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                referencesContainer.layoutElementRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                referencesContainer.layoutElementRT.ForceUpdateRectTransforms();
            }

            if (refreshAlignmentAndPosition)
            {
                RefreshDCLAlignmentAndPosition();
            }
        }

        public override void Dispose()
        {
            Utils.SafeDestroy(referencesContainer.gameObject);
            base.Dispose();
        }
    }
}
