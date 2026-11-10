using System;
using UnityEngine;

namespace TcgServer.Client
{
    [DisallowMultipleComponent]
    public sealed class TcgClientSession : MonoBehaviour
    {
        public static TcgClientSession Instance { get; private set; }

        public string Token { get; private set; }
        public PublicUserDto User { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && User != null;

        public event Action Changed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetSession(AuthSessionDto session)
        {
            Token = session?.token;
            User = session?.user;
            Changed?.Invoke();
        }

        public void Clear()
        {
            Token = null;
            User = null;
            Changed?.Invoke();
        }
    }
}

