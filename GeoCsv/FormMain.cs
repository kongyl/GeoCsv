using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GeoCsv
{
    public partial class FormMain : Form
    {
        public FormMain()
        {
            InitializeComponent();

            Gdal.AllRegister();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            Dispose();
        }

        private void buttonFolder_Click(object sender, EventArgs e)
        {
            textBoxFolder.Text = "";
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                textBoxFolder.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            string folder = textBoxFolder.Text;
            if (folder == null || folder.Trim().Equals(""))
            {
                MessageBox.Show("请选择数据路径");
                return;
            }

            toolStripStatusLabelState.Text = "正在处理";
            toolStripProgressBarPercent.Visible = true;
            toolStripStatusLabelPercent.Visible = true;
            statusStrip1.Update();

            // 获取元数据
            string[] tifs = Directory.GetFiles(folder, "*.tif");
            int tifNum = tifs.Length;
            string[] fields = new string[tifNum];
            Dataset[] datasets = new Dataset[tifNum];
            DataType[] dataTypes = new DataType[tifNum];
            double[] nodatas = new double[tifNum];
            for (int i = 0; i < tifNum; i++)
            {
                // field
                string[] paths = tifs[i].Split('\\');
                string filename = paths[paths.Length - 1];
                fields[i] = filename.Split('_')[0];
                // dataset
                datasets[i] = Gdal.Open(tifs[i], Access.GA_ReadOnly);
                dataTypes[i] = datasets[i].GetRasterBand(1).DataType;
                // nodata
                int hasVal;
                datasets[i].GetRasterBand(1).GetNoDataValue(out nodatas[i], out hasVal);
                if (hasVal != 1)
                {
                    nodatas[i] = double.NaN;
                }
            }
            double[] geoTransform = new double[6];
            datasets[0].GetGeoTransform(geoTransform);
            string proj = datasets[0].GetProjection();
            SpatialReference sr = new SpatialReference(proj);
            CoordinateTransformation trans = new CoordinateTransformation(sr, sr.CloneGeogCS());
            int isGeographic = sr.IsGeographic();
            double topLeftX = geoTransform[0];
            double xRes = geoTransform[1];
            double topLeftY = geoTransform[3];
            double yRes = geoTransform[5];
            int xSize = datasets[0].RasterXSize;
            int ySize = datasets[0].RasterYSize;
            int leftYSize = ySize;
            int yOffset = 0;
            int blockSize = 100;
            int progressValue = 0;

            // 写入csv
            using (StreamWriter sw = new StreamWriter(folder + "\\output.csv"))
            {
                sw.WriteLine(getHeader(fields));
                sw.Flush();
                while (leftYSize > 0) // 按行分块
                {
                    int readYSize = (leftYSize > blockSize) ? blockSize : leftYSize;
                    leftYSize = leftYSize - readYSize;
                    double[][] data = new double[tifNum][];

                    // 读入数据
                    for (int i = 0; i < tifNum; i++)
                    {
                        data[i] = new double[xSize * readYSize];
                        datasets[i].GetRasterBand(1).ReadRaster(0, yOffset, xSize, readYSize, data[i], xSize, readYSize, 0, 0);
                    }
                    for (int row = 0; row < readYSize; row++) // 按行
                    {
                        int rowNo = yOffset + row;
                        int current = rowNo * 100 / ySize;
                        if (current > progressValue) // 改变进度条状态
                        {
                            toolStripProgressBarPercent.Value = current;
                            toolStripStatusLabelPercent.Text = string.Format("{0}%", current);
                            statusStrip1.Update();
                            progressValue = current;
                        }
                        for (int col = 0; col < xSize; col++) // 按列
                        {
                            StringBuilder builder = new StringBuilder();
                            double[] latlon = new double[2];
                            latlon[0] = topLeftX + col * xRes;
                            latlon[1] = topLeftY + rowNo * yRes;
                            if (isGeographic == 0) // 投影转经纬度
                            {
                                trans.TransformPoint(latlon);
                            }
                            builder.Append(latlon[0].ToString("f6")).Append(",").Append(latlon[1].ToString("f6"));
                            bool isNoData = false;
                            for (int i = 0; i < tifNum; i++) // 按文件
                            {
                                builder.Append(",");
                                double v = data[i][row * xSize + col];
                                if (v == nodatas[i])
                                {
                                    isNoData = true;
                                    break;
                                }
                                if (dataTypes[i] == DataType.GDT_Float32 || dataTypes[i] == DataType.GDT_Float64)
                                {
                                    builder.Append(v.ToString("f6"));
                                }
                                else
                                {
                                    builder.Append(v.ToString("f0"));
                                }
                            }
                            if (!isNoData) // 非空值
                            {
                                sw.WriteLine(builder.ToString());
                            }
                        }
                        sw.Flush();
                    }

                    yOffset = yOffset + readYSize;
                }
                sw.Close();
            }

            // 释放资源
            for (int i = 0; i < tifNum; i++)
            {
                datasets[i].Dispose();
            }            
            toolStripStatusLabelState.Text = "就绪";
            toolStripProgressBarPercent.Visible = false;
            toolStripStatusLabelPercent.Visible = false;
            statusStrip1.Update();
            MessageBox.Show("处理完成");
        }

        private string getHeader(string[] fields)
        {
            StringBuilder builder = new StringBuilder("x,y");
            for (int i = 0; i < fields.Length; i++)
            {
                builder.Append(",").Append(fields[i]);
            }
            return builder.ToString();
        }

        private string getFieldType(DataType type)
        {
            switch (type)
            {
                case DataType.GDT_Byte:
                    return "byte";
                case DataType.GDT_UInt16:
                case DataType.GDT_Int16:
                    return "short";
                case DataType.GDT_UInt32:
                case DataType.GDT_Int32:
                    return "int";
                case DataType.GDT_Float32:
                    return "float";
                case DataType.GDT_Float64:
                    return "double";
                default:
                    return "other";
            }
        }
    }
}
