using System.Collections.Generic;
using DCL.Controllers;
using DCL.ECSRuntime;
using DCL.ECSRuntime.Tests;
using DCL.Models;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class SceneComponentsManagerShould
    {
        enum ComponentsID
        {
            Component0,
            Component1
        }

        IParcelScene scene;
        IComponentHandler<TestingComponent> componentHandler0;
        IComponentHandler<TestingComponent> componentHandler1;
        SceneComponentsManager componentsManager;

        [SetUp]
        public void SetUp()
        {
            scene = Substitute.For<IParcelScene>();
            componentHandler0 = Substitute.For<IComponentHandler<TestingComponent>>();
            componentHandler1 = Substitute.For<IComponentHandler<TestingComponent>>();

            Dictionary<int, ComponentsFactory.ECSComponentBuilder> components =
                new Dictionary<int, ComponentsFactory.ECSComponentBuilder>()
                {
                    {
                        (int)ComponentsID.Component0,
                        ComponentsFactory.DefineComponent(TestingComponentSerialization.Deserialize, () => componentHandler0)
                    },
                    {
                        (int)ComponentsID.Component1,
                        ComponentsFactory.DefineComponent(TestingComponentSerialization.Deserialize, () => componentHandler1)
                    },
                };

            componentsManager = new SceneComponentsManager(scene, components);
        }

        [Test]
        public void GetOrCreateComponent()
        {
            IDCLEntity entity = Substitute.For<IDCLEntity>();
            entity.entityId.Returns("1");

            IECSComponent comp0 = componentsManager.GetOrCreateComponent((int)ComponentsID.Component0, entity);
            IECSComponent comp1 = componentsManager.GetOrCreateComponent((int)ComponentsID.Component1, entity);

            componentHandler0.Received(1).OnComponentCreated(scene, entity);
            componentHandler1.Received(1).OnComponentCreated(scene, entity);

            componentHandler0.ClearReceivedCalls();
            componentHandler1.ClearReceivedCalls();

            Assert.NotNull(comp0);
            Assert.NotNull(comp1);
            Assert.AreNotEqual(comp0, comp1);

            Assert.IsTrue(comp0.HasComponent(entity));
            Assert.IsTrue(comp1.HasComponent(entity));

            IECSComponent comp0II = componentsManager.GetOrCreateComponent((int)ComponentsID.Component0, entity);
            IECSComponent comp1II = componentsManager.GetOrCreateComponent((int)ComponentsID.Component1, entity);

            componentHandler0.DidNotReceive().OnComponentCreated(scene, entity);
            componentHandler1.DidNotReceive().OnComponentCreated(scene, entity);

            Assert.NotNull(comp0II);
            Assert.NotNull(comp1II);

            Assert.AreEqual(comp0, comp0II);
            Assert.AreEqual(comp1, comp1II);

            Assert.AreEqual(comp0, componentsManager.sceneComponents[(int)ComponentsID.Component0]);
            Assert.AreEqual(comp1, componentsManager.sceneComponents[(int)ComponentsID.Component1]);

            Assert.AreEqual(2, componentsManager.sceneComponents.Count);
        }

        [Test]
        public void DeserializeComponent()
        {
            IDCLEntity entity = Substitute.For<IDCLEntity>();
            entity.entityId.Returns("1");

            var newComponentModel = new TestingComponent()
            {
                someString = "temptation",
                someVector = new Vector3(70, 0, -135)
            };
            var serializedModel = TestingComponentSerialization.Serialize(newComponentModel);

            componentsManager.DeserializeComponent((int)ComponentsID.Component0, entity, serializedModel);

            componentHandler0.Received(1).OnComponentCreated(scene, entity);
            componentHandler0.Received(1).OnComponentModelUpdated(scene, entity, Arg.Any<TestingComponent>());

            Assert.AreEqual(1, componentsManager.sceneComponents.Count);

            ECSComponent<TestingComponent> typedComponent = ((ECSComponent<TestingComponent>)componentsManager.sceneComponents[(int)ComponentsID.Component0]);
            Assert.AreEqual(newComponentModel.someString, typedComponent.Get(entity).model.someString);
            Assert.AreEqual(newComponentModel.someVector, typedComponent.Get(entity).model.someVector);
            Assert.IsTrue(typedComponent.HasComponent(entity));
        }

        [Test]
        public void RemoveComponent()
        {
            IDCLEntity entity = Substitute.For<IDCLEntity>();
            entity.entityId.Returns("1");

            componentsManager.GetOrCreateComponent((int)ComponentsID.Component0, entity);
            componentsManager.GetOrCreateComponent((int)ComponentsID.Component1, entity);

            componentsManager.RemoveComponent((int)ComponentsID.Component0, entity);
            componentsManager.RemoveComponent((int)ComponentsID.Component1, entity);

            componentHandler0.Received(1).OnComponentRemoved(scene, entity);
            componentHandler1.Received(1).OnComponentRemoved(scene, entity);

            ECSComponent<TestingComponent> typedComponent0 = ((ECSComponent<TestingComponent>)componentsManager.sceneComponents[(int)ComponentsID.Component0]);
            ECSComponent<TestingComponent> typedComponent1 = ((ECSComponent<TestingComponent>)componentsManager.sceneComponents[(int)ComponentsID.Component1]);

            Assert.IsFalse(typedComponent0.HasComponent(entity));
            Assert.IsFalse(typedComponent1.HasComponent(entity));

            Assert.AreEqual(0, typedComponent0.entities.Count);
            Assert.AreEqual(0, typedComponent1.entities.Count);
        }
    }
}