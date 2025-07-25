﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.Models.API
    {
    // 定义一个枚举来代表不同的API服务类型
    public enum ApiServiceType
        {
        Baidu,
        Gemini,
        OpenAI,
        Custom,
        SiliconFlow
        }

    public class ApiProfile : INotifyPropertyChanged
        {
        // ▼▼▼ 为需要通知UI更新的每个字段创建私有变量和公开属性 ▼▼▼
        private Guid _id = Guid.NewGuid();
        private string _profileName;
        private ApiServiceType _serviceType;
        private string _userId;
        private string _apiKey;
        private string _apiEndpoint;
        private List<string> _models = new List<string>();
        private string _lastSelectedModel;
        private List<string> _favoriteModels = new List<string>();

        public Guid Id { get => _id; set => SetField(ref _id, value); }
        public string ProfileName { get => _profileName; set => SetField(ref _profileName, value); }
        public ApiServiceType ServiceType { get => _serviceType; set => SetField(ref _serviceType, value); }
        public string UserId { get => _userId; set => SetField(ref _userId, value); }
        public string ApiKey { get => _apiKey; set => SetField(ref _apiKey, value); }
        public string ApiEndpoint { get => _apiEndpoint; set => SetField(ref _apiEndpoint, value); }
        public List<string> Models { get => _models; set => SetField(ref _models, value); }
        public string LastSelectedModel { get => _lastSelectedModel; set => SetField(ref _lastSelectedModel, value); }
        public List<string> FavoriteModels { get => _favoriteModels; set => SetField(ref _favoriteModels, value); }


        // ▼▼▼ 添加 INotifyPropertyChanged 的实现 ▼▼▼
        public event PropertyChangedEventHandler PropertyChanged;
        public ApiProfile() { }

        // 这就是我们的“复印机”功能
        public ApiProfile(ApiProfile other)
            {
            this.Id = other.Id;
            this.ProfileName = other.ProfileName;
            this.ServiceType = other.ServiceType;
            this.UserId = other.UserId;
            this.ApiKey = other.ApiKey;
            this.ApiEndpoint = other.ApiEndpoint;
            // 关键：为Models列表也创建一个新的副本，而不是共享引用
            this.Models = new List<string>(other.Models ?? new List<string>());
            this.LastSelectedModel = other.LastSelectedModel;
            this.FavoriteModels = new List<string>(other.FavoriteModels ?? new List<string>());
            }


        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }
        }
    }