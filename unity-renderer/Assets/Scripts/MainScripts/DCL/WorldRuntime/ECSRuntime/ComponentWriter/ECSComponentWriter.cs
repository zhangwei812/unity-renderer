using System;
using System.Collections.Generic;
using DCL.Controllers;
using DCL.Models;
using UnityEngine;

namespace DCL.ECSRuntime
{
    public class ECSComponentWriter
    {
        public delegate void WriteComponent(IParcelScene scene, IDCLEntity entity, int componentId, byte[] data);

        private readonly Dictionary<int, object> serializers = new Dictionary<int, object>();
        private readonly WriteComponent writeComponent;

        public ECSComponentWriter(WriteComponent writeComponent)
        {
            this.writeComponent = writeComponent;
        }

        public void AddOrReplaceComponentSerializer<T>(int componentId, Func<T, byte[]> serializer)
        {
            serializers[componentId] = serializer;
        }

        public void PutComponent<T>(IParcelScene scene, IDCLEntity entity, int componentId, T model)
        {
            if (!serializers.TryGetValue(componentId, out object serializer))
            {
                Debug.LogError($"Trying to write component but no serializer was found for {componentId}");
                return;
            }

            if (serializer is Func<T, byte[]> typedSerializer)
            {
                writeComponent(scene, entity, componentId, typedSerializer(model));
            }
            else
            {
                Debug.LogError($"Trying to write component but serializer for {componentId} does not match {nameof(T)}");
            }
        }

        public void RemoveComponent(IParcelScene scene, IDCLEntity entity, int componentId)
        {
            writeComponent(scene, entity, componentId, null);
        }
    }
}