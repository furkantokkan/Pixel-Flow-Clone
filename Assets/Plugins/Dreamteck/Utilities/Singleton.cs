namespace Dreamteck
{
    using UnityEngine;
    using ZLinq;

    public class Singleton<T> : PrivateSingleton<T> where T : Component
    {
        public static T instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Object.FindObjectsOfType<T>().AsValueEnumerable().FirstOrDefault();
                }

                return _instance;
            }
        }
    }
}
