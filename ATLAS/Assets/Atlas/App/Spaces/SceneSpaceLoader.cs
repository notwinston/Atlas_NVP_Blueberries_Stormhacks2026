using System.Collections.Generic;
using Atlas.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Atlas.App
{
    /// <summary>
    /// The scene-or-factory rule (contract README F3): a space's scene is loaded additively when it is in the
    /// build list; otherwise, or when the loaded scene holds no module, the module comes from the factory the
    /// space registered in SpaceRegistry and is parented under "Atlas/Space".
    /// </summary>
    public sealed class SceneSpaceLoader : ISpaceLoader
    {
        readonly Transform _spaceRoot;
        readonly List<GameObject> _roots = new List<GameObject>(4);

        SpaceId _target;
        AsyncOperation _load;
        // The module in the world: where it came from, so that it can be removed again.
        Scene _scene;
        bool _fromScene;

        public SceneSpaceLoader(Transform spaceRoot) { _spaceRoot = spaceRoot; }

        public bool IsAvailable(SpaceId id)
        {
            return SpaceIds.IsDefined(id) &&
                   (SpaceRegistry.IsRegistered(id) || Application.CanStreamedLevelBeLoaded(SpaceIds.SceneName(id)));
        }

        public void Begin(SpaceId id)
        {
            _target = id;
            _load = null;
            string scene = SpaceIds.SceneName(id);
            // Additive: Atlas_Boot stays the active scene.
            if (Application.CanStreamedLevelBeLoaded(scene))
                _load = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
        }

        public bool TryFinish(out ISpaceModule module)
        {
            module = null;
            if (_load != null)
            {
                if (!_load.isDone) return false;
                _load = null;
                Scene scene = SceneManager.GetSceneByName(SpaceIds.SceneName(_target));
                if (scene.IsValid() && scene.isLoaded)
                {
                    module = FindModule(scene);
                    if (module != null)
                    {
                        _scene = scene;
                        _fromScene = true;
                        return true;
                    }
                    // A scene with no module in it: drop it and fall through to the factory.
                    SceneManager.UnloadSceneAsync(scene);
                }
            }

            if (SpaceRegistry.IsRegistered(_target))
            {
                module = SpaceRegistry.Create(_target);
                var component = module as Component;
                if (component != null && _spaceRoot != null) component.transform.SetParent(_spaceRoot, false);
                _fromScene = false;
            }
            return true;
        }

        public void Unload(ISpaceModule module)
        {
            if (_fromScene)
            {
                _fromScene = false;
                if (_scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
                return;
            }
            var component = module as Component;
            if (component != null) AppObjects.Kill(component.gameObject);
        }

        ISpaceModule FindModule(Scene scene)
        {
            _roots.Clear();
            scene.GetRootGameObjects(_roots);
            ISpaceModule found = null;
            for (int i = 0; i < _roots.Count && found == null; i++)
                found = _roots[i].GetComponent<ISpaceModule>();
            _roots.Clear();
            return found;
        }
    }
}
