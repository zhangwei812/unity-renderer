using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DCL.Builder
{
    public interface IPublishProgressController
    {
        /// <summary>
        /// Fired when the user confirm the deployment
        /// </summary>
        event Action OnConfirm;

        void Initialize();

        void Dispose();

        /// <summary>
        /// This will show the confirmation pop up
        /// </summary>
        void ShowConfirmDeploy();

        /// <summary>
        /// This is called when the deployment has succeed
        /// </summary>
        /// <param name="publishedScene"></param>
        void DeploySuccess(IBuilderScene publishedScene);

        /// <summary>
        /// This is called when the deployment has failed with the error as parameter
        /// </summary>
        /// <param name="error"></param>
        void DeployError(string error);
    }

    public class PublishProgressController : IPublishProgressController
    {
        public event Action OnConfirm;

        private const string PROGRESS_PREFAB_PATH = "Project/PublishProgressView";
        private const string SUCCESS_PREFAB_PATH = "Project/PublishSuccessView";

        private const string GENERIC_DEPLOY_ERROR = "Error deploying the scene";

        internal IPublishProjectProgressView progressView;
        internal IPublishProjectSuccesView succesView;

        public void Initialize()
        {
            progressView = GameObject.Instantiate(Resources.Load<PublishProjectProgressView>(PROGRESS_PREFAB_PATH));
            succesView = GameObject.Instantiate(Resources.Load<PublishProjectSuccesView>(SUCCESS_PREFAB_PATH));

            progressView.OnViewClosed += ViewClosed;
            progressView.OnPublishConfirmButtonPressed += ConfirmPressed;

            succesView.OnViewClose += ViewClosed;
        }

        public void Dispose()
        {
            progressView.OnViewClosed -= ViewClosed;
            progressView.OnPublishConfirmButtonPressed -= ConfirmPressed;

            succesView.OnViewClose -= ViewClosed;

            progressView.Dispose();
            succesView.Dispose();
        }

        public void ShowConfirmDeploy() { progressView.ShowConfirmPopUp(); }

        private void ConfirmPressed()
        {
            OnConfirm?.Invoke();
            progressView.PublishStarted();
        }

        public void DeploySuccess(IBuilderScene publishedScene)
        {
            progressView.Hide();
            succesView.ProjectPublished(publishedScene);
        }

        public void DeployError(string error) { progressView.PublishError(error); }

        private void ViewClosed()
        {
            progressView.Hide();
            succesView.Hide();
        }
    }
}