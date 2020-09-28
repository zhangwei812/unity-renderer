﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityGLTF;
using UnityGLTF.Cache;

namespace DCL
{
    public abstract partial class ABConverter
    {
        public class Core
        {
            public enum ErrorCodes
            {
                SUCCESS = 0,
                UNDEFINED = 1,
                SCENE_LIST_NULL = 2,
                ASSET_BUNDLE_BUILD_FAIL = 3,
            }

            public class State
            {
                public enum Step
                {
                    IDLE,
                    DUMPING_ASSETS,
                    BUILDING_ASSET_BUNDLES,
                    FINISHED,
                }

                public Step step { get; internal set; }
                public ErrorCodes lastErrorCode { get; internal set; }
            }

            public readonly State state = new State();

            private const string MAIN_SHADER_AB_NAME = "MainShader_Delete_Me";

            public Dictionary<string, string> hashLowercaseToHashProper = new Dictionary<string, string>();

            internal readonly string finalDownloadedPath;

            public Client.Settings settings;

            private float startTime;
            private int totalAssets;
            private int skippedAssets;

            private Environment env;
            private static Logger log = new Logger("ABConverter.Core");
            private string logBuffer;

            public Core(Environment env, Client.Settings settings = null)
            {
                this.env = env;

                this.settings = settings?.Clone() ?? new Client.Settings();

                finalDownloadedPath = PathUtils.FixDirectorySeparator(Config.DOWNLOADED_PATH_ROOT + Config.DASH);
                log.verboseEnabled = this.settings.verbose;

                state.step = State.Step.IDLE;
            }

            /// <summary>
            /// Generate asset bundles using a MappingPair list.
            ///
            /// This method will try to dump GLTF models, textures and buffers from the given mapping pair list,
            /// tag them for asset bundle building and finally build them.
            ///
            /// If the GLTF have external references of any texture/buffer of the same list, the references will be
            /// resolved correctly on the GLTF importer and then correctly converted to Asset Bundles references.
            ///
            /// Shader assets will be stripped from the generated bundles.
            /// </summary>
            /// <param name="rawContents">A list detailing assets to be dumped</param>
            /// <param name="OnFinish">End callback with the proper ErrorCode</param>
            public void Convert(ContentServerUtils.MappingPair[] rawContents, Action<ErrorCodes> OnFinish = null)
            {
                if (OnFinish == null)
                    OnFinish = CleanAndExit;
                else
                    OnFinish += CleanAndExit;

                startTime = Time.realtimeSinceStartup;

                log.Info($"Conversion start... free space in disk: {PathUtils.GetFreeSpace()}");

                InitializeDirectoryPaths(true);
                PopulateLowercaseMappings(rawContents);

                float timer = Time.realtimeSinceStartup;
                bool shouldGenerateAssetBundles = true;
                bool assetsAlreadyDumped = false;

                //TODO(Brian): Use async-await instead of Application.update
                void UpdateLoop()
                {
                    try
                    {
                        //NOTE(Brian): We have to check this because the ImportAsset for GLTFs is not synchronous, and must execute some delayed calls
                        //             after the import asset finished. Therefore, we have to make sure those calls finished before continuing.
                        if (!GLTFImporter.finishedImporting && Time.realtimeSinceStartup - timer < 60) return;

                        env.assetDatabase.Refresh();

                        if (!assetsAlreadyDumped)
                        {
                            state.step = State.Step.DUMPING_ASSETS;
                            shouldGenerateAssetBundles |= DumpAssets(rawContents);
                            assetsAlreadyDumped = true;
                            timer = Time.realtimeSinceStartup;

                            //NOTE(Brian): return in order to wait for GLTFImporter.finishedImporting flag, as it will set asynchronously.
                            return;
                        }

                        EditorApplication.update -= UpdateLoop;

                        if (shouldGenerateAssetBundles)
                        {
                            AssetBundleManifest manifest;

                            state.step = State.Step.BUILDING_ASSET_BUNDLES;

                            if (BuildAssetBundles(out manifest))
                            {
                                CleanAssetBundleFolder(manifest.GetAllAssetBundles());

                                state.lastErrorCode = ErrorCodes.SUCCESS;
                                state.step = State.Step.FINISHED;
                                OnFinish?.Invoke(state.lastErrorCode);
                            }
                            else
                            {
                                state.lastErrorCode = ErrorCodes.ASSET_BUNDLE_BUILD_FAIL;
                                state.step = State.Step.FINISHED;
                                OnFinish?.Invoke(state.lastErrorCode);
                            }
                        }
                        else
                        {
                            state.lastErrorCode = ErrorCodes.SUCCESS;
                            state.step = State.Step.FINISHED;
                            OnFinish?.Invoke(state.lastErrorCode);
                        }
                    }
                    catch (Exception e)
                    {
                        log.Error(e.Message + "\n" + e.StackTrace);
                        state.lastErrorCode = ErrorCodes.UNDEFINED;
                        state.step = State.Step.FINISHED;
                        OnFinish?.Invoke(state.lastErrorCode);
                        EditorApplication.update -= UpdateLoop;
                    }
                }

                EditorApplication.update += UpdateLoop;
            }


            /// <summary>
            /// Dump all assets and tag them for asset bundle building.
            /// </summary>
            /// <param name="rawContents">An array containing all the assets to be dumped.</param>
            /// <returns>true if succeeded</returns>
            private bool DumpAssets(ContentServerUtils.MappingPair[] rawContents)
            {
                List<AssetPath> gltfPaths = ABConverter.Utils.GetPathsFromPairs(finalDownloadedPath, rawContents, Config.gltfExtensions);
                List<AssetPath> bufferPaths = ABConverter.Utils.GetPathsFromPairs(finalDownloadedPath, rawContents, Config.bufferExtensions);
                List<AssetPath> texturePaths = ABConverter.Utils.GetPathsFromPairs(finalDownloadedPath, rawContents, Config.textureExtensions);

                List<AssetPath> assetsToMark = new List<AssetPath>();

                if (!PrepareDump(ref gltfPaths))
                    return false;

                //NOTE(Brian): Prepare textures and buffers. We should prepare all the dependencies in this phase.
                assetsToMark.AddRange(DumpImportableAssets(texturePaths));
                DumpRawAssets(bufferPaths);

                GLTFImporter.OnGLTFRootIsConstructed -= ABConverter.Utils.FixGltfRootInvalidUriCharacters;
                GLTFImporter.OnGLTFRootIsConstructed += ABConverter.Utils.FixGltfRootInvalidUriCharacters;

                foreach (var gltfPath in gltfPaths)
                {
                    assetsToMark.Add(DumpGltf(gltfPath, texturePaths, bufferPaths));
                }

                env.assetDatabase.Refresh();
                env.assetDatabase.SaveAssets();

                MarkAllAssetBundles(assetsToMark);
                MarkShaderAssetBundle();
                return true;
            }

            /// <summary>
            /// Trims off existing asset bundles from the given AssetPath array,
            /// if none exists and shouldAbortBecauseAllBundlesExist is true, it will return false.
            /// </summary>
            /// <param name="gltfPaths">paths to be checked for existence</param>
            /// <returns>false if all paths are already converted to asset bundles, true if the conversion makes sense</returns>
            internal bool PrepareDump(ref List<AssetPath> gltfPaths)
            {
                bool shouldAbortBecauseAllBundlesExist = true;

                totalAssets += gltfPaths.Count;

                if (settings.skipAlreadyBuiltBundles)
                {
                    int gltfCount = gltfPaths.Count;

                    gltfPaths = gltfPaths.Where(
                        assetPath =>
                            !env.file.Exists(settings.finalAssetBundlePath + assetPath.hash)).ToList();

                    int skippedCount = gltfCount - gltfPaths.Count;
                    skippedAssets += skippedCount;
                    shouldAbortBecauseAllBundlesExist = gltfPaths.Count == 0;
                }
                else
                {
                    shouldAbortBecauseAllBundlesExist = false;
                }

                if (shouldAbortBecauseAllBundlesExist)
                {
                    log.Info("All assets in this scene were already generated!. Skipping.");
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Dump a single gltf asset injecting the proper external references
            /// </summary>
            /// <param name="gltfPath">GLTF to be dumped</param>
            /// <param name="texturePaths">array with texture dependencies</param>
            /// <param name="bufferPaths">array with buffer dependencies</param>
            /// <returns>gltf AssetPath if dump succeeded, null if don't</returns>
            internal AssetPath DumpGltf(AssetPath gltfPath, List<AssetPath> texturePaths, List<AssetPath> bufferPaths)
            {
                List<Stream> streamsToDispose = new List<Stream>();

                PersistentAssetCache.ImageCacheByUri.Clear();
                PersistentAssetCache.StreamCacheByUri.Clear();

                log.Verbose("Start injecting stuff into " + gltfPath.hash);

                //NOTE(Brian): Prepare gltfs gathering its dependencies first and filling the importer's static cache.
                foreach (var texturePath in texturePaths)
                {
                    RetrieveAndInjectTexture(gltfPath, texturePath);
                }

                foreach (var bufferPath in bufferPaths)
                {
                    RetrieveAndInjectBuffer(gltfPath, bufferPath);
                }

                log.Verbose("About to load " + gltfPath.hash);

                //NOTE(Brian): Load the gLTF after the dependencies are injected.
                //             The GLTFImporter will use the PersistentAssetCache to resolve them.
                string path = DownloadAsset(gltfPath);

                if (path != null)
                {
                    env.assetDatabase.Refresh();
                    env.assetDatabase.SaveAssets();
                }

                log.Verbose("End load " + gltfPath.hash);

                foreach (var streamDataKvp in PersistentAssetCache.StreamCacheByUri)
                {
                    if (streamDataKvp.Value.stream != null)
                        streamsToDispose.Add(streamDataKvp.Value.stream);
                }

                foreach (var s in streamsToDispose)
                {
                    s.Dispose();
                }

                return path != null ? gltfPath : null;
            }

            /// <summary>
            /// Download assets and put them in the working folder.
            /// </summary>
            /// <param name="bufferPaths">AssetPath list containing all the desired paths to be dumped</param>
            /// <returns>List of the successfully dumped assets.</returns>
            internal List<AssetPath> DumpRawAssets(List<AssetPath> bufferPaths)
            {
                List<AssetPath> result = new List<AssetPath>(bufferPaths);

                if (bufferPaths.Count == 0 || bufferPaths == null)
                    return result;

                foreach (var assetPath in bufferPaths)
                {
                    if (env.file.Exists(assetPath.finalPath))
                        continue;

                    var finalDlPath = DownloadAsset(assetPath);

                    if (string.IsNullOrEmpty(finalDlPath))
                    {
                        result.Remove(assetPath);
                        log.Error("Failed to get buffer dependencies! failing asset: " + assetPath.hash);
                    }
                }

                return result;
            }

            /// <summary>
            /// This will dump all assets contained in the AssetPath list using the baseUrl + hash.
            ///
            /// After the assets are dumped, they will be imported using Unity's AssetDatabase and
            /// their guids will be normalized using the asset's cid.
            ///
            /// The guid normalization will ensure the guids remain consistent and the same asset will
            /// always have the asset guid. If we don't normalize the guids, Unity will chose a random one,
            /// and this can break the Asset Bundles dependencies as they are resolved by guid.
            /// </summary>
            /// <param name="assetPaths">List of assetPaths to be dumped</param>
            /// <returns>A list with assetPaths that were successfully dumped. This list will be empty if all dumps failed.</returns>
            internal List<AssetPath> DumpImportableAssets(List<AssetPath> assetPaths)
            {
                List<AssetPath> result = new List<AssetPath>(assetPaths);

                foreach (var assetPath in assetPaths)
                {
                    if (env.file.Exists(assetPath.finalPath))
                        continue;

                    //NOTE(Brian): try to get an AB before getting the original texture, so we bind the dependencies correctly
                    string fullPathToTag = DownloadAsset(assetPath);

                    if (fullPathToTag == null)
                    {
                        result.Remove(assetPath);
                        log.Error("Failed to get texture dependencies! failing asset: " + assetPath.hash);
                        continue;
                    }

                    env.assetDatabase.ImportAsset(assetPath.finalPath, ImportAssetOptions.ForceUpdate);
                    env.assetDatabase.SaveAssets();

                    SetDeterministicAssetDatabaseGuid(assetPath);

                    log.Verbose($"Dumping file -> {assetPath}");
                }

                return result;
            }

            /// <summary>
            /// This will download a single asset referenced by an AssetPath.
            /// The download target is baseUrl + hash.
            /// </summary>
            /// <param name="assetPath">The AssetPath object referencing the asset to be downloaded</param>
            /// <returns>The file output path. Null if download failed.</returns>
            internal string DownloadAsset(AssetPath assetPath)
            {
                string outputPath = assetPath.finalPath;
                string outputPathDir = Path.GetDirectoryName(outputPath);
                string finalUrl = settings.baseUrl + assetPath.hash;

                if (env.file.Exists(outputPath))
                {
                    log.Verbose("Skipping already generated asset: " + outputPath);
                    return outputPath;
                }

                DownloadHandler downloadHandler = null;

                try
                {
                    downloadHandler = env.webRequest.Get(finalUrl);

                    if (downloadHandler == null)
                    {
                        log.Error($"Download failed! {finalUrl} -- null DownloadHandler");
                        return null;
                    }
                }
                catch (HttpRequestException e)
                {
                    log.Error($"Download failed! {finalUrl} -- {e.Message}");
                    return null;
                }

                byte[] assetData = downloadHandler.data;
                downloadHandler.Dispose();

                log.Verbose($"Downloaded asset = {finalUrl} to {outputPath}");

                if (!env.directory.Exists(outputPathDir))
                    env.directory.CreateDirectory(outputPathDir);

                env.file.WriteAllBytes(outputPath, assetData);
                env.assetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ImportRecursive);

                return outputPath;
            }

            /// <summary>
            /// Load dumped textures and put them in PersistentAssetCache so the GLTFSceneImporter
            /// can pick them up.
            /// </summary>
            /// <param name="gltfPath">GLTF path of the gltf that will pick up the references</param>
            /// <param name="texturePath">Texture path of the texture to be injected</param>
            internal void RetrieveAndInjectTexture(AssetPath gltfPath, AssetPath texturePath)
            {
                string finalPath = texturePath.finalPath;

                if (!env.file.Exists(finalPath))
                    return;

                Texture2D t2d = env.assetDatabase.LoadAssetAtPath<Texture2D>(finalPath);

                if (t2d == null)
                    return;

                string relativePath = ABConverter.PathUtils.GetRelativePathTo(gltfPath.file, texturePath.file);

                //NOTE(Brian): This cache will be used by the GLTF importer when seeking textures. This way the importer will
                //             consume the asset bundle dependencies instead of trying to create new textures.
                PersistentAssetCache.AddImage(relativePath, gltfPath.finalPath, new RefCountedTextureData(relativePath, t2d));
            }

            /// <summary>
            /// Load dumped buffers and put them in PersistentAssetCache so the GLTFSceneImporter
            /// can pick them up.
            /// </summary>
            /// <param name="gltfPath">GLTF path of the gltf that will pick up the references</param>
            /// <param name="bufferPath">Buffer path of the texture to be injected</param>
            internal void RetrieveAndInjectBuffer(AssetPath gltfPath, AssetPath bufferPath)
            {
                string finalPath = bufferPath.finalPath;

                if (!env.file.Exists(finalPath))
                    return;

                Stream stream = env.file.OpenRead(finalPath);
                string relativePath = ABConverter.PathUtils.GetRelativePathTo(gltfPath.file, bufferPath.file);

                // NOTE(Brian): This cache will be used by the GLTF importer when seeking streams. This way the importer will
                //              consume the asset bundle dependencies instead of trying to create new streams.
                PersistentAssetCache.AddBuffer(relativePath, gltfPath.finalPath, new RefCountedStreamData(relativePath, stream));
            }

            /// <summary>
            /// Mark all the given assetPaths to be built as asset bundles by Unity's BuildPipeline.
            /// </summary>
            /// <param name="assetPaths">The paths to be built.</param>
            private void MarkAllAssetBundles(List<AssetPath> assetPaths)
            {
                foreach (var assetPath in assetPaths)
                {
                    ABConverter.Utils.MarkFolderForAssetBundleBuild(assetPath.finalPath, assetPath.hash);
                }
            }

            /// <summary>
            /// Build all marked paths as asset bundles using Unity's BuildPipeline and generate their .depmap files
            /// </summary>
            /// <param name="manifest">AssetBundleManifest generated by the build.</param>
            /// <returns>true is build was successful</returns>
            protected virtual bool BuildAssetBundles(out AssetBundleManifest manifest)
            {
                env.assetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                env.assetDatabase.SaveAssets();

                env.assetDatabase.MoveAsset(finalDownloadedPath, Config.DOWNLOADED_PATH_ROOT);

                manifest = env.buildPipeline.BuildAssetBundles(settings.finalAssetBundlePath, BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.WebGL);

                if (manifest == null)
                {
                    log.Error("Error generating asset bundle!");
                    return false;
                }

                DependencyMapBuilder.Generate(env.file, settings.finalAssetBundlePath, hashLowercaseToHashProper, manifest, MAIN_SHADER_AB_NAME);
                logBuffer += $"Generating asset bundles at path: {settings.finalAssetBundlePath}\n";

                string[] assetBundles = manifest.GetAllAssetBundles();

                logBuffer += $"Total generated asset bundles: {assetBundles.Length}\n";

                for (int i = 0; i < assetBundles.Length; i++)
                {
                    if (string.IsNullOrEmpty(assetBundles[i]))
                        continue;

                    logBuffer += $"#{i} Generated asset bundle name: {assetBundles[i]}\n";
                }

                logBuffer += $"\nFree disk space after conv: {PathUtils.GetFreeSpace()}";
                return true;
            }

            /// <summary>
            /// Clean all working folders and end the batch process.
            /// </summary>
            /// <param name="errorCode">final errorCode of the conversion process</param>
            private void CleanAndExit(ErrorCodes errorCode)
            {
                float conversionTime = Time.realtimeSinceStartup - startTime;
                logBuffer = $"Conversion finished!. error code = {errorCode}";

                logBuffer += "\n";
                logBuffer += $"Converted {totalAssets - skippedAssets} of {totalAssets}. (Skipped {skippedAssets})\n";
                logBuffer += $"Total time: {conversionTime}";

                if (totalAssets > 0)
                {
                    logBuffer += $"... Time per asset: {conversionTime / totalAssets}\n";
                }

                logBuffer += "\n";
                logBuffer += logBuffer;

                log.Info(logBuffer);

                CleanupWorkingFolders();
                Utils.Exit((int) errorCode);
            }

            /// <summary>
            /// in asset bundles, all dependencies are resolved by their guid (and not the AB hash nor CRC)
            /// So to ensure dependencies are being kept in subsequent editor runs we normalize the asset guid using
            /// the CID.
            ///
            /// This method:
            /// - Looks for the meta file of the given assetPath.
            /// - Changes the .meta guid using the assetPath's cid as seed.
            /// - Does some file system gymnastics to make sure the new guid is imported to our AssetDatabase.
            /// </summary>
            /// <param name="assetPath">AssetPath of the target asset to modify</param>
            private void SetDeterministicAssetDatabaseGuid(AssetPath assetPath)
            {
                string metaPath = env.assetDatabase.GetTextMetaFilePathFromAssetPath(assetPath.finalPath);

                env.assetDatabase.ReleaseCachedFileHandles();

                string metaContent = env.file.ReadAllText(metaPath);
                string guid = ABConverter.Utils.CidToGuid(assetPath.hash);
                string newMetaContent = Regex.Replace(metaContent, @"guid: \w+?\n", $"guid: {guid}\n");

                //NOTE(Brian): We must do this hack in order to the new guid to be added to the AssetDatabase.
                //             on windows, an AssetImporter.SaveAndReimport call makes the trick, but this won't work
                //             on Unix based OSes for some reason.
                env.file.Delete(metaPath);

                env.file.Copy(assetPath.finalPath, finalDownloadedPath + "tmp");
                env.assetDatabase.DeleteAsset(assetPath.finalPath);
                env.file.Delete(assetPath.finalPath);

                env.assetDatabase.Refresh();
                env.assetDatabase.SaveAssets();

                env.file.Copy(finalDownloadedPath + "tmp", assetPath.finalPath);
                env.file.WriteAllText(metaPath, newMetaContent);
                env.file.Delete(finalDownloadedPath + "tmp");

                env.assetDatabase.Refresh();
                env.assetDatabase.SaveAssets();
            }


            internal void CleanAssetBundleFolder(string[] assetBundles)
            {
                ABConverter.Utils.CleanAssetBundleFolder(env.file, settings.finalAssetBundlePath, assetBundles, hashLowercaseToHashProper);
            }


            internal void PopulateLowercaseMappings(ContentServerUtils.MappingPair[] pairs)
            {
                foreach (var content in pairs)
                {
                    string hashLower = content.hash.ToLowerInvariant();

                    if (!hashLowercaseToHashProper.ContainsKey(hashLower))
                        hashLowercaseToHashProper.Add(hashLower, content.hash);
                }
            }

            /// <summary>
            /// This method tags the main shader, so all the asset bundles don't contain repeated shader assets.
            /// This way we save the big Shader.Parse and gpu compiling performance overhead and make
            /// the bundles a bit lighter.
            /// </summary>
            private void MarkShaderAssetBundle()
            {
                //NOTE(Brian): The shader asset bundle that's going to be generated doesn't need to be really used,
                //             as we are going to use the embedded one, so we are going to just delete it after the
                //             generation ended.
                var mainShader = Shader.Find("DCL/LWRP/Lit");
                ABConverter.Utils.MarkAssetForAssetBundleBuild(env.assetDatabase, mainShader, MAIN_SHADER_AB_NAME);
            }


            internal virtual void InitializeDirectoryPaths(bool deleteIfExists)
            {
                env.directory.InitializeDirectory(finalDownloadedPath, deleteIfExists);
                env.directory.InitializeDirectory(settings.finalAssetBundlePath, deleteIfExists);
            }

            internal void CleanupWorkingFolders()
            {
                env.file.Delete(settings.finalAssetBundlePath + Config.ASSET_BUNDLE_FOLDER_NAME);
                env.file.Delete(settings.finalAssetBundlePath + Config.ASSET_BUNDLE_FOLDER_NAME + ".manifest");

                if (settings.deleteDownloadPathAfterFinished)
                {
                    env.directory.Delete(finalDownloadedPath);
                    env.assetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
        }
    }
}