# loxodon-framework-mvi介绍
## 首先感谢loxodon-framework框架作者， loxodon-framework是一个MVVM双向数据绑定的用于Unity开发的框架，在代码质量、可读性、性能等方面非常优秀。
https://github.com/vovgou/loxodon-framework
### MVI架构图如下
![alt text](mad-arch-ui-udf.png)
MVI架构是谷歌最新的UI架构，是在MVVM基础上解决一些生产环境的痛点而产生单项数据、响应式、不可变状态的新型框架，目前主要是在Android原生上使用的比较多，Unity以及其他方面基本没有，所以才创建了loxodon-framework-mvi库。
###
loxodon-framework-mvi在loxodon-framework框架上进行扩展实现MVI架构，没有修改loxodon-framework任何代码，通过Nuget进行包管理引用loxodon-framework，实现了响应式编程、单数据流、不可变状态，主要依赖如下开源库实现：
### R3 https://github.com/Cysharp/R3
### loxodon-framework-mvi 类介绍
#### IIntent意图类，用于执行一系列的意图
#### IMviResult结果类，用于生成意图的结果
#### IState状态类，表示UI进行显式的状态信息
#### MviViewModel类是继承loxodon-framework框架的ViewNModelBase类，ViewNModelBase类是用来处理业务逻辑的，MVVM所有的业务逻辑基本都写在ViewModel中
#### Store类是管理状态的更新，用于生成新的状态

## Demo演示
打开Unity工程找到Samples\Loxodon Framework\2.0.0\Examples\Launcher场景，直接运行即可，该项目工程是在官方Demo基础上进行修改，具体可以进行对比，使用MVI架构后ViewModel和View之间只存在绑定关系不存在业务逻辑关系，所有的业务逻辑都分发到Intent中