using Mapper;
using MVI;
using UnityEngine;

namespace Loxodon.Framework.Examples.Scripts.Mapper
{
    public class MapperInitializer
    {
        [RuntimeInitializeOnLoadMethod]
        static void Initialize()
        {
            // 游戏启动时配置所有映射
            LightMapper.CreateMap<LoginFailureState, LoginViewModel>();
            LightMapper.CreateMap<LoginSuccessState, LoginViewModel>();
            LightMapper.CreateMap<IState, MviViewModel>();
        }
    }
}