using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Strategies;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 分割策略管理器
/// 管理和选择不同的分割策略
/// </summary>
public class SplitStrategyManager
{
    private readonly Dictionary<string, ISplitConditionStrategy> _strategies;

    public SplitStrategyManager()
    {
        _strategies = new Dictionary<string, ISplitConditionStrategy>
        {
            { "sentence", new SentenceEndingSplitStrategy() },
            { "length", new LengthBasedSplitStrategy() }
        };
    }

    /// <summary>
    /// 注册新的分割策略
    /// </summary>
    public void RegisterStrategy(string key, ISplitConditionStrategy strategy)
    {
        _strategies[key.ToLower()] = strategy;
    }

    /// <summary>
    /// 获取指定的分割策略
    /// </summary>
    public ISplitConditionStrategy GetStrategy(string strategyName)
    {
        var key = strategyName?.ToLower() ?? "sentence";
        return _strategies.TryGetValue(key, out var strategy) ? strategy : _strategies["sentence"];
    }

    /// <summary>
    /// 获取所有可用的策略
    /// </summary>
    public Dictionary<string, ISplitConditionStrategy> GetAllStrategies()
    {
        return new Dictionary<string, ISplitConditionStrategy>(_strategies);
    }

    /// <summary>
    /// 智能选择最合适的分割策略
    /// </summary>
    public ISplitConditionStrategy SelectBestStrategy(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return _strategies["sentence"];

        // 优先检查是否有明显的句子结构
        if (_strategies["sentence"].ShouldSplit(text, config))
        {
            return _strategies["sentence"];
        }

        // 检查是否适合长度分割
        if (_strategies["length"].ShouldSplit(text, config))
        {
            return _strategies["length"];
        }

        // 默认返回句子分割策略
        return _strategies["sentence"];
    }
}