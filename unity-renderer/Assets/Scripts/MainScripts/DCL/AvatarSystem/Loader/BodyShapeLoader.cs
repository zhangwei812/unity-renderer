using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DCL;
using UnityEngine;
using UnityEngine.Assertions;

namespace AvatarSystem
{
    public class BodyShapeLoader : IBodyshapeLoader
    {
        public WearableItem wearable { get; }
        public Rendereable rendereable => bodyshapeRetriever?.rendereable;
        public IWearableLoader.Status status { get; private set; }
        public WearableItem eyes { get; }
        public WearableItem eyebrows { get; }
        public WearableItem mouth { get; }
        public SkinnedMeshRenderer eyesRenderer { get; private set; }
        public SkinnedMeshRenderer eyebrowsRenderer { get; private set; }
        public SkinnedMeshRenderer mouthRenderer { get; private set; }
        public SkinnedMeshRenderer headRenderer { get; private set; }
        public SkinnedMeshRenderer feetRenderer { get; private set; }
        public SkinnedMeshRenderer upperBodyRenderer { get; private set; }
        public SkinnedMeshRenderer lowerBodyRenderer { get; private set; }

        private readonly IWearableRetriever bodyshapeRetriever;
        private readonly IFacialFeatureRetriever eyesRetriever;
        private readonly IFacialFeatureRetriever eyebrowsRetriever;
        private readonly IFacialFeatureRetriever mouthRetriever;

        public BodyShapeLoader(IRetrieverFactory retrieverFactory, WearableItem bodyshape, WearableItem eyes, WearableItem eyebrows, WearableItem mouth)
        {
            Assert.AreEqual(WearableLiterals.Categories.BODY_SHAPE, bodyshape.data.category);
            Assert.AreEqual(WearableLiterals.Categories.EYES, eyes.data.category);
            Assert.AreEqual(WearableLiterals.Categories.EYEBROWS, eyebrows.data.category);
            Assert.AreEqual(WearableLiterals.Categories.MOUTH, mouth.data.category);

            wearable = bodyshape;
            this.eyes = eyes;
            this.eyebrows = eyebrows;
            this.mouth = mouth;

            bodyshapeRetriever = retrieverFactory.GetWearableRetriever();
            eyesRetriever = retrieverFactory.GetFacialFeatureRetriever();
            eyebrowsRetriever = retrieverFactory.GetFacialFeatureRetriever();
            mouthRetriever = retrieverFactory.GetFacialFeatureRetriever();
        }

        public async UniTask Load(GameObject container, AvatarSettings avatarSettings)
        {
            status = IWearableLoader.Status.Idle;

            //TODO reuse bodyshape if succeeded
            await LoadWearable(container);
            if (rendereable == null)
            {
                status = IWearableLoader.Status.Failed;
                return;
            }

            (headRenderer, upperBodyRenderer, lowerBodyRenderer, feetRenderer, eyesRenderer, eyebrowsRenderer, mouthRenderer) = AvatarSystemUtils.ExtractBodyshapeParts(bodyshapeRetriever.rendereable);
            headRenderer.enabled = avatarSettings.headVisible;
            lowerBodyRenderer.enabled = avatarSettings.lowerbodyVisible;
            upperBodyRenderer.enabled = avatarSettings.upperbodyVisible;
            feetRenderer.enabled = avatarSettings.feetVisible;
            eyesRenderer.enabled = true;
            eyebrowsRenderer.enabled = true;
            mouthRenderer.enabled = true;

            AvatarSystemUtils.PrepareMaterialColors(rendereable, avatarSettings.skinColor, avatarSettings.hairColor);

            (string eyesMainTextureUrl, string eyesMaskTextureUrl) = AvatarSystemUtils.GetFacialFeatureTexturesUrls(wearable.id, eyes);
            (string eyebrowsMainTextureUrl, string eyebrowsMaskTextureUrl) = AvatarSystemUtils.GetFacialFeatureTexturesUrls(wearable.id, eyebrows);
            (string mouthMainTextureUrl, string mouthMaskTextureUrl) = AvatarSystemUtils.GetFacialFeatureTexturesUrls(wearable.id, mouth);

            UniTask<(Texture main, Texture mask)> eyesTask = eyesRetriever.Retrieve(eyesMainTextureUrl, eyesMaskTextureUrl);
            UniTask<(Texture main, Texture mask)> eyebrowsTask = eyebrowsRetriever.Retrieve(eyebrowsMainTextureUrl, eyebrowsMaskTextureUrl);
            UniTask<(Texture main, Texture mask)> mouthTask = mouthRetriever.Retrieve(mouthMainTextureUrl, mouthMaskTextureUrl);

            var (eyesResult, eyebrowsResult, mouthResult) = await (eyesTask, eyebrowsTask, mouthTask);

            eyesRenderer.material = new Material(Resources.Load<Material>("Eye Material"));
            eyesRenderer.material.SetTexture(AvatarSystemUtils._EyesTexture, eyesResult.main);
            if (eyesResult.mask != null)
                eyesRenderer.material.SetTexture(AvatarSystemUtils._IrisMask, eyesResult.mask);
            eyesRenderer.material.SetColor(AvatarSystemUtils._EyeTint, avatarSettings.eyesColor);

            eyebrowsRenderer.material = new Material(Resources.Load<Material>("Eyebrow Material"));
            eyebrowsRenderer.material.SetTexture(AvatarSystemUtils._BaseMap, eyebrowsResult.main);
            if (eyebrowsResult.mask != null)
                eyebrowsRenderer.material.SetTexture(AvatarSystemUtils._BaseMap, eyebrowsResult.mask);
            eyebrowsRenderer.material.SetColor(AvatarSystemUtils._BaseColor, avatarSettings.hairColor);

            mouthRenderer.material = new Material(Resources.Load<Material>("Mouth Material"));
            mouthRenderer.material.SetTexture(AvatarSystemUtils._BaseMap, mouthResult.main);
            if (mouthResult.mask != null)
                mouthRenderer.material.SetTexture(AvatarSystemUtils._TintMask, mouthResult.mask);
            mouthRenderer.material.SetColor(AvatarSystemUtils._BaseColor, avatarSettings.skinColor);
        }

        private async UniTask<Rendereable> LoadWearable(GameObject container)
        {
            bodyshapeRetriever.Dispose();

            var representation = wearable.GetRepresentation(wearable.id);
            if (representation == null)
            {
                status = IWearableLoader.Status.Failed;
                return null;
            }
            return await bodyshapeRetriever.Retrieve(container, wearable.GetContentProvider(wearable.id), wearable.baseUrlBundles, representation.mainFile);
        }

        public List<SkinnedMeshRenderer> GetEnabledBodyparts()
        {
            List<SkinnedMeshRenderer> result = new List<SkinnedMeshRenderer>();
            if (headRenderer.enabled)
                result.Add(headRenderer);
            if (upperBodyRenderer.enabled)
                result.Add(upperBodyRenderer);
            if (lowerBodyRenderer.enabled)
                result.Add(lowerBodyRenderer);
            if (feetRenderer.enabled)
                result.Add(feetRenderer);
            return result;
        }

        public void Dispose()
        {
            bodyshapeRetriever?.Dispose();
            eyesRetriever?.Dispose();
            eyebrowsRetriever?.Dispose();
            mouthRetriever?.Dispose();

            if (eyesRenderer != null)
                Object.Destroy(eyesRenderer.material);
            if (eyebrowsRenderer != null)
                Object.Destroy(eyebrowsRenderer.material);
            if (mouthRenderer != null)
                Object.Destroy(mouthRenderer.material);

            upperBodyRenderer = null;
            lowerBodyRenderer = null;
            feetRenderer = null;
            headRenderer = null;
            eyesRenderer = null;
            eyebrowsRenderer = null;
            mouthRenderer = null;
        }
    }
}