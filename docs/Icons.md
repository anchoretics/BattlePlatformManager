# 图标维护

`src/BPM.App/Resources/Icons.xaml` 是图标的矢量源：14 个 24 单位坐标的操作图标，以及“独立账号卡片 + 启动”应用标识。按钮图标继承按钮前景色，保留文字及可访问名称，不依赖字体图标包或特定 Windows 字体版本。

`src/BPM.App/Assets/BPM.ico` 同时用于 EXE、任务栏及窗口，包含 16、20、24、32、40、48、64、128、256 像素的 32 位图像。窗口内标识直接使用矢量 DrawingImage。

在 Windows 上修改矢量后，重新生成 ICO：

```powershell
dotnet run --project tools/BPM.IconBuilder/BPM.IconBuilder.csproj -- src/BPM.App/Resources/Icons.xaml src/BPM.App/Assets/BPM.ico
```

同目录生成的 `BPM.preview.png` 用于查看图标集，不参与程序运行。图形为项目原创，随项目采用 MIT 许可证。
