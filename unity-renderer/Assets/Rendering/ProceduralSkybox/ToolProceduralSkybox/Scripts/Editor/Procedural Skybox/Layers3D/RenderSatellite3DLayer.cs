using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DCL.Skybox
{
    public static class RenderSatellite3DLayer
    {
        public static void RenderLayer(ref float timeOfTheDay, EditorToolMeasurements toolSize, SkyboxConfiguration config)
        {
            RenderSimpleValues.RenderPrefabInput("Prefab", ref config.satelliteLayer.satellite);
            RenderSimpleValues.RenderFloatField("Size", ref config.satelliteLayer.satelliteSize);
            RenderSimpleValues.RenderFloatFieldAsSlider("Initial Pos", ref config.satelliteLayer.initialAngle, 0, 360);
            RenderSimpleValues.RenderFloatFieldAsSlider("Horizon Plane", ref config.satelliteLayer.horizonPlaneRotation, 0, 180);
            RenderSimpleValues.RenderFloatFieldAsSlider("Inclination", ref config.satelliteLayer.inclination, 0, 180);
            RenderSimpleValues.RenderFloatField("Speed", ref config.satelliteLayer.movementSpeed);
            RenderSimpleValues.RenderEnumPopup<RotationType>("Rotation Type", ref config.satelliteLayer.satelliteRotation);

            // If fixed rotation
            if (config.satelliteLayer.satelliteRotation == RotationType.Fixed)
            {
                RenderSimpleValues.RenderVector3Field("Rotation", ref config.satelliteLayer.fixedRotation);
            }
            else if (config.satelliteLayer.satelliteRotation == RotationType.Rotate)
            {
                RenderSimpleValues.RenderVector3Field("Axis", ref config.satelliteLayer.rotateAroundAxis);
                RenderSimpleValues.RenderFloatField("Rotation Speed", ref config.satelliteLayer.rotateSpeed);
            }
        }
    }
}