using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Interfaces;

/// <summary>
/// �ָ��������Խӿ�
/// ʵ�ֿ���ԭ��������չ��ͬ�ķָ�����
/// </summary>
public interface ISplitConditionStrategy
{
    /// <summary>
    /// ��������
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// ��������
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// �ж��ı��Ƿ�����ָ�����
    /// </summary>
    /// <param name="text">Ҫ�������ı�</param>
    /// <param name="config">�ָ�����</param>
    /// <returns>�������ָ���������true</returns>
    bool ShouldSplit(string text, SplitterConfig config);
    
    /// <summary>
    /// ���ı������ض������ָ�ɾ���Ƭ��
    /// </summary>
    /// <param name="text">ԭʼ�ı�</param>
    /// <param name="originalSegment">ԭʼ��Ƶ��</param>
    /// <param name="config">�ָ�����</param>
    /// <returns>�ָ�����Ƶ���б�</returns>
    List<AudioSegment> SplitText(string text, AudioSegment originalSegment, SplitterConfig config);
}