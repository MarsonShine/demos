using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Interfaces;

/// <summary>
/// 分割条件策略接口
/// 实现开闭原则，允许扩展不同的分割条件
/// </summary>
public interface ISplitConditionStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 策略描述
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 判断文本是否满足分割条件
    /// </summary>
    /// <param name="text">要分析的文本</param>
    /// <param name="config">分割配置</param>
    /// <returns>如果满足分割条件返回true</returns>
    bool ShouldSplit(string text, SplitterConfig config);
    
    /// <summary>
    /// 将文本按照特定条件分割成句子片段
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <param name="originalSegment">原始音频段</param>
    /// <param name="config">分割配置</param>
    /// <returns>分割后的音频段列表</returns>
    List<AudioSegment> SplitText(string text, AudioSegment originalSegment, SplitterConfig config);
}