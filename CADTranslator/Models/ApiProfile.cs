using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.Models
    {
    // 定义一个枚举来代表不同的API服务类型
    public enum ApiServiceType
        {
        Baidu,
        Gemini,
        OpenAI,
        Custom
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

        public Guid Id { get => _id; set => SetField(ref _id, value); }
        public string ProfileName { get => _profileName; set => SetField(ref _profileName, value); }
        public ApiServiceType ServiceType { get => _serviceType; set => SetField(ref _serviceType, value); }
        public string UserId { get => _userId; set => SetField(ref _userId, value); }
        public string ApiKey { get => _apiKey; set => SetField(ref _apiKey, value); }
        public string ApiEndpoint { get => _apiEndpoint; set => SetField(ref _apiEndpoint, value); }
        public List<string> Models { get => _models; set => SetField(ref _models, value); }
        public string LastSelectedModel { get => _lastSelectedModel; set => SetField(ref _lastSelectedModel, value); }


        // ▼▼▼ 添加 INotifyPropertyChanged 的实现 ▼▼▼
        public event PropertyChangedEventHandler PropertyChanged;

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