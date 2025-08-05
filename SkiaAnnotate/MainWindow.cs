using System.ComponentModel;
using System.Windows;
using SkiaSharp;

namespace SkiaAnnotate
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// 基于SkiaSharp的屏幕批注工具主窗口
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 字段和属性

        /// <summary>
        /// 当前绘制的画布
        /// </summary>
        private SKCanvas? _currentCanvas;

        /// <summary>
        /// 当前绘制的表面
        /// </summary>
        private SKSurface? _currentSurface;

        /// <summary>
        /// 绘制历史记录
        /// </summary>
        private readonly List<SKPath> _drawingHistory = new();

        /// <summary>
        /// 当前选中的绘制工具
        /// </summary>
        public enum DrawingTool
        {
            Pen,
            Rectangle,
            Circle,
            Line,
            Text,
            Eraser
        }

        private DrawingTool _currentTool = DrawingTool.Pen;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化主窗口
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            InitializeSkiaCanvas();
            SetupEventHandlers();
        }

        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化SkiaSharp画布
        /// </summary>
        private void InitializeSkiaCanvas()
        {
            // TODO: 初始化SkiaSharp画布
            // 这里将添加画布初始化代码
        }

        /// <summary>
        /// 设置事件处理器
        /// </summary>
        private void SetupEventHandlers()
        {
            // TODO: 设置各种事件处理器
            // 包括鼠标事件、键盘事件等
        }

        #endregion

        #region 事件处理方法

        /// <summary>
        /// 窗口加载完成事件
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="e">事件参数</param>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // TODO: 窗口加载完成后的初始化工作
        }

        /// <summary>
        /// 窗口关闭事件
        /// </summary>
        /// <param name="sender">事件源</param>
        /// <param name="e">事件参数</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // TODO: 清理资源
            CleanupResources();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 清理资源
        /// </summary>
        private void CleanupResources()
        {
            _currentCanvas?.Dispose();
            _currentSurface?.Dispose();
            _drawingHistory.Clear();
        }

        #endregion

        #region 主栏按钮事件

        // 鼠标按钮：切换为鼠标指针（选择/无操作模式）
        private void MouseButton_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = DrawingTool.Pen; // 这里可根据实际需求设为None或Pointer
            // TODO: 切换为鼠标指针模式
        }

        // 批注按钮：切换为画笔模式
        private void PenButton_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = DrawingTool.Pen;
            // TODO: 切换为批注（画笔）模式
        }

        // 清空按钮：清空画布
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _drawingHistory.Clear();
            // TODO: 触发画布重绘，清空所有批注
        }

        // 白板按钮：新建白板
        private void WhiteboardButton_Click(object sender, RoutedEventArgs e)
        {
            _drawingHistory.Clear();
            // TODO: 触发画布重绘，显示空白白板
        }

        // 工具按钮：弹出工具菜单（可扩展）
        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 弹出工具菜单或面板
        }

        // 隐藏按钮：隐藏主栏
        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            // TODO: 可通过快捷键或其他方式重新显示主栏
        }

        #endregion
    }
}
