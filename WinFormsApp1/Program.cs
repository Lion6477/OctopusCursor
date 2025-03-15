using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace TentacleOverlay
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TentacleForm());
        }
    }

    public class TentacleForm : Form
    {
        // Параметры щупалец
        private const int SegmentCount = 15;      // Количество щупалец
        private const int PointsPerTentacle = 8;  // Точки для каждого щупальца
        private const float SegmentLength = 30f;  // Длина сегмента
        private const float SmoothSpeed = 0.15f;  // Скорость сглаживания
        private const float WobbleAmplitude = 10f;// Амплитуда колебания
        private const float WobbleFrequency = 2f; // Частота колебания
        private const float MaxGrabDistance = 300f; // Максимальное расстояние захвата
        private const float DetachDistance = 350f; // Расстояние отцепления
        private const float PredictionTimeMs = 500f; // Время предсказания движения (мс)

        private PointF[] targetPoints;    // Точки захвата
        private PointF[][] tentaclePoints; // Точки щупалец
        private PointF lastMousePos;      // Последняя позиция мыши
        private PointF mouseVelocity;     // Скорость движения мыши
        private DateTime lastUpdateTime;  // Время последнего обновления

        private Timer updateTimer;        // Таймер обновления

        public TentacleForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            // Инициализация переменных
            PointF startPos = Cursor.Position;
            lastMousePos = startPos;
            mouseVelocity = new PointF(0, 0);
            lastUpdateTime = DateTime.Now;

            // Инициализация точек захвата
            targetPoints = new PointF[SegmentCount];
            ResetAllTargetPoints(startPos);

            // Инициализация точек щупалец
            tentaclePoints = new PointF[SegmentCount][];
            for (int i = 0; i < SegmentCount; i++)
            {
                tentaclePoints[i] = new PointF[PointsPerTentacle];
                for (int j = 0; j < PointsPerTentacle; j++)
                {
                    tentaclePoints[i][j] = startPos;
                }
            }

            // Настройка таймера
            updateTimer = new Timer();
            updateTimer.Interval = 44; // ~60 FPS
            updateTimer.Tick += UpdateTentacles;
            updateTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20;     // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x80000;  // WS_EX_LAYERED
                return cp;
            }
        }

        private void UpdateTentacles(object sender, EventArgs e)
        {
            // Обновление скорости мыши
            PointF currentMousePos = Cursor.Position;
            DateTime currentTime = DateTime.Now;
            float deltaTime = (float)(currentTime - lastUpdateTime).TotalSeconds;
            
            if (deltaTime > 0)
            {
                // Расчет скорости с небольшим сглаживанием
                mouseVelocity.X = (currentMousePos.X - lastMousePos.X) / deltaTime * 0.3f + mouseVelocity.X * 0.7f;
                mouseVelocity.Y = (currentMousePos.Y - lastMousePos.Y) / deltaTime * 0.3f + mouseVelocity.Y * 0.7f;
            }
            
            lastMousePos = currentMousePos;
            lastUpdateTime = currentTime;

            // Прогнозируемая позиция курсора
            PointF predictedPos = new PointF(
                currentMousePos.X + mouseVelocity.X * (PredictionTimeMs / 1000f),
                currentMousePos.Y + mouseVelocity.Y * (PredictionTimeMs / 1000f)
            );

            // Обновление щупалец
            for (int i = 0; i < SegmentCount; i++)
            {
                // Проверка расстояния до точки захвата
                float distToTarget = Distance(currentMousePos, targetPoints[i]);
                
                // Если расстояние слишком большое, выбираем новую точку захвата
                if (distToTarget > DetachDistance)
                {
                    targetPoints[i] = GetRandomPointNear(predictedPos, MaxGrabDistance);
                }

                // Обновление точек щупальца
                UpdateTentaclePoints(i, currentMousePos);
            }

            this.Invalidate();
        }

        private void UpdateTentaclePoints(int tentacleIndex, PointF startPoint)
        {
            PointF endPoint = targetPoints[tentacleIndex];
            
            // Расчет основных точек щупальца
            for (int i = 0; i < PointsPerTentacle; i++)
            {
                // Интерполяция от начала к концу
                float t = (float)i / (PointsPerTentacle - 1);
                PointF basePoint = Lerp(startPoint, endPoint, t);
                
                // Добавление волнообразного движения
                float wavePhase = GetTime() * WobbleFrequency + tentacleIndex;
                float waveAmplitude = WobbleAmplitude * (float)Math.Sin(t * Math.PI); // Максимум в середине
                
                // Направление перпендикулярное к линии между начальной и конечной точками
                float dx = endPoint.X - startPoint.X;
                float dy = endPoint.Y - startPoint.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                
                // Нормализация и поворот на 90 градусов
                if (length > 0.001f)
                {
                    float nx = -dy / length;
                    float ny = dx / length;
                    
                    // Применение волны
                    float offset = waveAmplitude * (float)Math.Sin(wavePhase + t * 4 * Math.PI);
                    PointF target = new PointF(
                        basePoint.X + nx * offset,
                        basePoint.Y + ny * offset
                    );
                    
                    // Сглаживание движения
                    tentaclePoints[tentacleIndex][i] = Lerp(tentaclePoints[tentacleIndex][i], target, SmoothSpeed);
                }
                else
                {
                    tentaclePoints[tentacleIndex][i] = basePoint;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            // Отрисовка щупалец
            for (int i = 0; i < SegmentCount; i++)
            {
                // Градиент от синего к фиолетовому
                Color startColor = Color.FromArgb(0, 100, 255);
                Color endColor = Color.FromArgb(150, 0, 255);
                
                // Рисуем линии между точками
                for (int j = 0; j < PointsPerTentacle - 1; j++)
                {
                    // Интерполяция цвета
                    float t = (float)j / (PointsPerTentacle - 1);
                    Color segmentColor = InterpolateColor(startColor, endColor, t);
                    
                    // Толщина линии уменьшается к концу
                    float thickness = 5f * (1f - t * 0.7f);
                    
                    using (Pen pen = new Pen(segmentColor, thickness))
                    {
                        g.DrawLine(pen, tentaclePoints[i][j], tentaclePoints[i][j + 1]);
                    }
                }
                
                // Рисуем точку захвата (для отладки)
                //g.FillEllipse(Brushes.Red, targetPoints[i].X - 3, targetPoints[i].Y - 3, 6, 6);
            }
        }

        private void ResetAllTargetPoints(PointF center)
        {
            for (int i = 0; i < SegmentCount; i++)
            {
                targetPoints[i] = GetRandomPointNear(center, MaxGrabDistance);
            }
        }

        private PointF GetRandomPointNear(PointF center, float radius)
        {
            Random random = new Random();
            double angle = random.NextDouble() * 2 * Math.PI;
            double distance = random.NextDouble() * radius;
            
            return new PointF(
                center.X + (float)(Math.Cos(angle) * distance),
                center.Y + (float)(Math.Sin(angle) * distance)
            );
        }

        private float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t
            );
        }

        private Color InterpolateColor(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t)
            );
        }

        private float GetTime()
        {
            return (float)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        }
    }
}