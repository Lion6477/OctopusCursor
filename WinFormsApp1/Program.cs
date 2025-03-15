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
        // Профили обновления экрана (FPS)
        private readonly Dictionary<string, int> refreshProfiles = new Dictionary<string, int>
        {
            { "30 FPS", 33 },
            { "60 FPS", 16 },
            { "120 FPS", 8 },
            { "144 FPS", 7 },
            { "165 FPS", 6 },
            { "240 FPS", 4 }
        };
        private string currentProfile = "60 FPS";

        // Параметры щупалец
        private const int SegmentCount = 15;      // Количество щупалец
        private const int PointsPerTentacle = 10; // Точки для каждого щупальца
        private const float SegmentLength = 30f;  // Длина сегмента
        private const float SmoothSpeed = 0.15f;  // Скорость сглаживания
        private const float WobbleAmplitude = 6f; // Амплитуда колебания
        private const float WobbleFrequency = 1.5f; // Частота колебания
        private const float MaxGrabDistance = 200f; // Максимальное расстояние захвата
        private const float DetachDistance = 250f; // Расстояние отцепления
        private const float PredictionTimeMs = 300f; // Время предсказания движения (мс)
        private const float GravityStrength = 0.8f; // Сила провисания (гравитация)
        private const float MovementThreshold = 2.0f; // Порог движения мыши

        private PointF[] targetPoints;    // Точки захвата
        private PointF[][] tentaclePoints; // Точки щупалец
        private PointF lastMousePos;      // Последняя позиция мыши
        private PointF mouseVelocity;     // Скорость движения мыши
        private DateTime lastUpdateTime;  // Время последнего обновления
        private bool isMoving = false;    // Флаг движения курсора

        private Timer updateTimer;        // Таймер обновления
        private ContextMenuStrip contextMenu; // Контекстное меню

        public TentacleForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            // Создаем контекстное меню
            CreateContextMenu();
            
            // Добавляем обработчик правого клика
            this.MouseClick += (s, e) => {
                if (e.Button == MouseButtons.Right)
                {
                    contextMenu.Show(this, e.Location);
                }
            };

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
            updateTimer.Interval = refreshProfiles[currentProfile];
            updateTimer.Tick += UpdateTentacles;
            updateTimer.Start();
        }

        private void CreateContextMenu()
        {
            contextMenu = new ContextMenuStrip();
            
            // Добавляем меню профилей
            ToolStripMenuItem profilesMenu = new ToolStripMenuItem("Частота обновления");
            foreach (var profile in refreshProfiles.Keys)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(profile);
                item.Checked = (profile == currentProfile);
                item.Click += (s, e) => {
                    // Снимаем выделение со всех
                    foreach (ToolStripMenuItem menuItem in profilesMenu.DropDownItems)
                    {
                        menuItem.Checked = false;
                    }
                    
                    // Выбираем текущий
                    ((ToolStripMenuItem)s).Checked = true;
                    currentProfile = ((ToolStripMenuItem)s).Text;
                    updateTimer.Interval = refreshProfiles[currentProfile];
                };
                profilesMenu.DropDownItems.Add(item);
            }
            contextMenu.Items.Add(profilesMenu);
            
            // Добавляем выход
            contextMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) => Application.Exit();
            contextMenu.Items.Add(exitItem);
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
                float dx = (currentMousePos.X - lastMousePos.X);
                float dy = (currentMousePos.Y - lastMousePos.Y);
                
                // Определяем, движется ли мышь
                isMoving = Math.Sqrt(dx * dx + dy * dy) > MovementThreshold;
                
                mouseVelocity.X = dx / deltaTime * 0.3f + mouseVelocity.X * 0.7f;
                mouseVelocity.Y = dy / deltaTime * 0.3f + mouseVelocity.Y * 0.7f;
            }
            
            lastMousePos = currentMousePos;
            lastUpdateTime = currentTime;

            // Прогнозируемая позиция курсора только при движении
            PointF predictedPos;
            if (isMoving)
            {
                float velocityMagnitude = (float)Math.Sqrt(mouseVelocity.X * mouseVelocity.X + mouseVelocity.Y * mouseVelocity.Y);
                float maxPredictionDistance = MaxGrabDistance * 0.7f; // Не больше 70% от MaxGrabDistance
                
                // Ограничиваем предсказание
                float predictionScale = PredictionTimeMs / 1000f;
                if (velocityMagnitude * predictionScale > maxPredictionDistance)
                {
                    predictionScale = maxPredictionDistance / velocityMagnitude;
                }
                
                predictedPos = new PointF(
                    currentMousePos.X + mouseVelocity.X * predictionScale,
                    currentMousePos.Y + mouseVelocity.Y * predictionScale
                );
            }
            else
            {
                predictedPos = currentMousePos;
            }

            // Обновление щупалец
            for (int i = 0; i < SegmentCount; i++)
            {
                // Проверка расстояния до точки захвата только если движемся
                if (isMoving)
                {
                    float distToTarget = Distance(currentMousePos, targetPoints[i]);
                    
                    // Если расстояние слишком большое, выбираем новую точку захвата
                    if (distToTarget > DetachDistance)
                    {
                        targetPoints[i] = GetRandomPointNear(predictedPos, MaxGrabDistance);
                    }
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
                
                // Добавляем провисание из-за гравитации (параболический эффект)
                float gravitySag = GravityStrength * t * (1 - t) * 4; // Максимум в середине
                
                // Базовая точка с учетом гравитации
                PointF basePoint;
                
                float dx = endPoint.X - startPoint.X;
                float dy = endPoint.Y - startPoint.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                
                if (length > 0.001f)
                {
                    // Нормализуем
                    dx /= length;
                    dy /= length;
                    
                    // Перпендикулярный вектор (для волны)
                    float nx = -dy;
                    float ny = dx;
                    
                    // Базовая точка с учетом линейной интерполяции
                    basePoint = new PointF(
                        startPoint.X + dx * length * t,
                        startPoint.Y + dy * length * t
                    );
                    
                    // Добавляем гравитацию
                    basePoint.Y += gravitySag * 20; // Увеличиваем эффект провисания
                    
                    // Добавление волнообразного движения только при движении
                    if (isMoving)
                    {
                        float wavePhase = GetTime() * WobbleFrequency + tentacleIndex;
                        float waveAmplitude = WobbleAmplitude * (float)Math.Sin(t * Math.PI);
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
                        // Более плавное движение при неподвижном курсоре
                        tentaclePoints[tentacleIndex][i] = Lerp(tentaclePoints[tentacleIndex][i], basePoint, SmoothSpeed * 0.5f);
                    }
                }
                else
                {
                    tentaclePoints[tentacleIndex][i] = startPoint;
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
                // Небесно-голубой цвет разных оттенков
                Color startColor = Color.FromArgb(135, 206, 250); // LightSkyBlue
                Color endColor = Color.FromArgb(65, 105, 225);    // RoyalBlue
                
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
            }
        }

        private void ResetAllTargetPoints(PointF center)
        {
            Random random = new Random();
            for (int i = 0; i < SegmentCount; i++)
            {
                double angle = i * (2 * Math.PI / SegmentCount);
                double distance = random.NextDouble() * MaxGrabDistance;
                
                targetPoints[i] = new PointF(
                    center.X + (float)(Math.Cos(angle) * distance),
                    center.Y + (float)(Math.Sin(angle) * distance)
                );
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
                (int)(a.B + (b.B - a.B