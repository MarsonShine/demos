using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Strategies;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// �ָ���Թ�����
/// �����ѡ��ͬ�ķָ����
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
    /// ע���µķָ����
    /// </summary>
    public void RegisterStrategy(string key, ISplitConditionStrategy strategy)
    {
        _strategies[key.ToLower()] = strategy;
    }

    /// <summary>
    /// ��ȡָ���ķָ����
    /// </summary>
    public ISplitConditionStrategy GetStrategy(string strategyName)
    {
        var key = strategyName?.ToLower() ?? "sentence";
        return _strategies.TryGetValue(key, out var strategy) ? strategy : _strategies["sentence"];
    }

    /// <summary>
    /// ��ȡ���п��õĲ���
    /// </summary>
    public Dictionary<string, ISplitConditionStrategy> GetAllStrategies()
    {
        return new Dictionary<string, ISplitConditionStrategy>(_strategies);
    }

    /// <summary>
    /// ����ѡ������ʵķָ����
    /// </summary>
    public ISplitConditionStrategy SelectBestStrategy(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return _strategies["sentence"];

        // ���ȼ���Ƿ������Եľ��ӽṹ
        if (_strategies["sentence"].ShouldSplit(text, config))
        {
            return _strategies["sentence"];
        }

        // ����Ƿ��ʺϳ��ȷָ�
        if (_strategies["length"].ShouldSplit(text, config))
        {
            return _strategies["length"];
        }

        // Ĭ�Ϸ��ؾ��ӷָ����
        return _strategies["sentence"];
    }
}