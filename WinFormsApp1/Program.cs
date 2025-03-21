using System.Diagnostics;
using System.Drawing.Drawing2D;
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
        // Базовые параметры щупалец
        private const int SegmentCount = 10; // Количество щупалец
        private const int PointsPerTentacle = 80; // Точки для каждого щупальца
        private const float SegmentLength = 30f; // Длина сегмента
        private const float SmoothSpeed = 0.15f; // Скорость сглаживания
        private const float MaxGrabDistance = 300f; // Максимальное расстояние захвата
        private const float DetachDistance = 350f; // Расстояние отцепления
        private const float PredictionTimeMs = 500f; // Время предсказания движения (мс)

        // Профили обновления экрана (FPS)
        private enum RefreshProfile
        {
            FPS30 = 33, // ~30 FPS
            FPS60 = 16, // ~60 FPS
            FPS120 = 8, // ~120 FPS
            FPS144 = 7, // ~144 FPS
            FPS165 = 6, // ~165 FPS
            FPS240 = 4 // ~240 FPS
        }

        // Настраиваемые параметры
        private enum WobbleIntensity
        {
            None = 0,
            Low = 1,
            Medium = 2,
            High = 3,
            VeryHigh = 4
        }

        private enum GravityIntensity
        {
            VeryLow = 1,
            Low = 2,
            Medium = 3,
            High = 4,
            VeryHigh = 5
        }

        private enum TentacleStyle
        {
            Tapered, // Сужающееся щупальце
            Uniform, // Монотонная толщина
            TaperedWithTip // Сужающееся с наконечником
        }

        // Настройки по умолчанию
        private RefreshProfile currentProfile = RefreshProfile.FPS165;
        private WobbleIntensity wobbleIntensity = WobbleIntensity.None;
        private bool wobbleWhileIdle = false;
        private GravityIntensity gravityIntensity = GravityIntensity.Medium;
        private TentacleStyle tentacleStyle = TentacleStyle.Tapered;

        // Рассчитываемые параметры на основе настроек
        private float wobbleAmplitude => wobbleIntensity switch
        {
            WobbleIntensity.None => 0f,
            WobbleIntensity.Low => 5f,
            WobbleIntensity.Medium => 10f,
            WobbleIntensity.High => 15f,
            WobbleIntensity.VeryHigh => 25f,
            _ => 0f
        };

        private float wobbleFrequency => wobbleIntensity switch
        {
            WobbleIntensity.None => 0f,
            WobbleIntensity.Low => 1f,
            WobbleIntensity.Medium => 2f,
            WobbleIntensity.High => 3f,
            WobbleIntensity.VeryHigh => 4f,
            _ => 0f
        };

        private float gravityEffect => gravityIntensity switch
        {
            GravityIntensity.VeryLow => 0.05f,
            GravityIntensity.Low => 0.1f,
            GravityIntensity.Medium => 0.2f,
            GravityIntensity.High => 0.3f,
            GravityIntensity.VeryHigh => 0.4f,
            _ => 0.2f
        };

        private PointF[] targetPoints; // Точки захвата
        private PointF[][] tentaclePoints; // Точки щупалец
        private PointF lastMousePos; // Последняя позиция мыши
        private PointF mouseVelocity; // Скорость движения мыши
        private DateTime lastUpdateTime; // Время последнего обновления
        private Random random; // Генератор случайных чисел
        private bool isMouseMoving; // Флаг движения мыши

        private Timer updateTimer; // Таймер обновления

        public TentacleForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            // Добавляем обработчик клавиш для выхода и смены профиля
            this.KeyPreview = true;
            this.KeyDown += TentacleForm_KeyDown;

            // Инициализация переменных
            PointF startPos = new PointF(Cursor.Position.X, Cursor.Position.Y);
            lastMousePos = startPos;
            mouseVelocity = new PointF(0, 0);
            lastUpdateTime = DateTime.Now;
            random = new Random();
            isMouseMoving = false;

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
            updateTimer.Interval = (int)currentProfile;
            updateTimer.Tick += UpdateTentacles;
            updateTimer.Start();
        }

        private void TentacleForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Выход по Escape
            if (e.KeyCode == Keys.Escape)
            {
                Application.Exit();
            }

            // Смена профиля по клавишам F1-F6
            if (e.KeyCode == Keys.F1)
                SetRefreshProfile(RefreshProfile.FPS30);
            else if (e.KeyCode == Keys.F2)
                SetRefreshProfile(RefreshProfile.FPS60);
            else if (e.KeyCode == Keys.F3)
                SetRefreshProfile(RefreshProfile.FPS120);
            else if (e.KeyCode == Keys.F4)
                SetRefreshProfile(RefreshProfile.FPS144);
            else if (e.KeyCode == Keys.F5)
                SetRefreshProfile(RefreshProfile.FPS165);
            else if (e.KeyCode == Keys.F6)
                SetRefreshProfile(RefreshProfile.FPS240);
        }

        private void SetRefreshProfile(RefreshProfile profile)
        {
            currentProfile = profile;
            updateTimer.Interval = (int)profile;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                return cp;
            }
        }

        private void UpdateTentacles(object sender, EventArgs e)
        {
            // Обновление скорости мыши
            PointF currentMousePos = Cursor.Position;
            DateTime currentTime = DateTime.Now;
            float deltaTime = (float)(currentTime - lastUpdateTime).TotalSeconds;

            // Определяем, движется ли мышь
            bool wasPreviouslyMoving = isMouseMoving;
            isMouseMoving = !(currentMousePos.X == lastMousePos.X && currentMousePos.Y == lastMousePos.Y);

            if (deltaTime > 0)
            {
                // Плавное сглаживание при быстрых движениях
                float smoothFactor = Math.Min(0.7f, deltaTime * 10f);
                float inputFactor = 1 - smoothFactor;
                
                mouseVelocity.X = (currentMousePos.X - lastMousePos.X) / deltaTime * inputFactor + mouseVelocity.X * smoothFactor;
                mouseVelocity.Y = (currentMousePos.Y - lastMousePos.Y) / deltaTime * inputFactor + mouseVelocity.Y * smoothFactor;
            }

            // Если мышь не двигается, обнуляем скорость
            if (!isMouseMoving)
            {
                mouseVelocity.X *= 0.95f; // Плавное затухание
                mouseVelocity.Y *= 0.95f;

                // Если скорость меньше порога, считаем что движения нет
                if (Math.Abs(mouseVelocity.X) < 0.1f && Math.Abs(mouseVelocity.Y) < 0.1f)
                {
                    mouseVelocity.X = 0;
                    mouseVelocity.Y = 0;
                }
            }

            lastMousePos = currentMousePos;
            lastUpdateTime = currentTime;

            // Расчет скорости курсора
            float speed = (float)Math.Sqrt(mouseVelocity.X * mouseVelocity.X + mouseVelocity.Y * mouseVelocity.Y);
            
            // Адаптивное прогнозирование - чем выше скорость, тем меньше время прогноза
            float adaptivePredictionFactor = 1.0f / (1.0f + speed * 0.005f);
            float effectivePredictionTime = PredictionTimeMs * adaptivePredictionFactor;
            
            // Ограничение дистанции предсказания
            float predictDistance = Math.Min(speed * (effectivePredictionTime / 1000f), MaxGrabDistance * 0.7f);

            // Прогнозируемая позиция с учетом направления
            float directionX = speed > 0.1f ? mouseVelocity.X / speed : 0;
            float directionY = speed > 0.1f ? mouseVelocity.Y / speed : 0;

            // Используем вектор направления для прогнозирования позиции впереди курсора
            PointF predictedPos = new PointF(
                currentMousePos.X + directionX * predictDistance,
                currentMousePos.Y + directionY * predictDistance
            );

            // Обновление щупалец
            for (int i = 0; i < SegmentCount; i++)
            {
                // Проверка расстояния до точки захвата
                float distToTarget = Distance(currentMousePos, targetPoints[i]);

                // Выбираем новую точку захвата при определенных условиях
                if (distToTarget > DetachDistance || 
                    (speed > 15 && Vector2Distance(
                        new PointF(mouseVelocity.X, mouseVelocity.Y),
                        new PointF(targetPoints[i].X - currentMousePos.X, targetPoints[i].Y - currentMousePos.Y)) > Math.PI/2))
                {
                    targetPoints[i] = GetRandomPointNear(predictedPos, predictDistance);
                }

                // Обновление точек щупальца
                UpdateTentaclePoints(i, currentMousePos);
            }

            this.Invalidate();
        }

        private float Vector2Distance(PointF a, PointF b)
        {
            float dot = a.X * b.X + a.Y * b.Y;
            float magA = (float)Math.Sqrt(a.X * a.X + a.Y * a.Y);
            float magB = (float)Math.Sqrt(b.X * b.X + b.Y * b.Y);
            return (float)Math.Acos(dot / (magA * magB + 0.0001f));
        }

        private void UpdateTentaclePoints(int tentacleIndex, PointF startPoint)
        {
            PointF endPoint = targetPoints[tentacleIndex];

            // Расчет основных точек щупальца
            for (int i = 0; i < PointsPerTentacle; i++)
            {
                // Интерполяция от начала к концу
                float t = (float)i / (PointsPerTentacle - 1);

                // Добавляем провисание (эффект гравитации)
                float gravEffect = (float)Math.Sin(t * Math.PI) * gravityEffect;

                // Базовая точка с учетом гравитации
                PointF basePoint = new PointF(
                    Lerp(startPoint.X, endPoint.X, t),
                    Lerp(startPoint.Y, endPoint.Y, t) + gravEffect * Distance(startPoint, endPoint)
                );

                // Инициализируем амплитуду волны с учётом настроек
                float currentWobbleAmplitude = wobbleAmplitude;

                // Если мышь не двигается и отключены волны в состоянии покоя
                if (!isMouseMoving && !wobbleWhileIdle)
                {
                    currentWobbleAmplitude = 0;
                }

                // Добавление волнообразного движения
                float wavePhase = GetTime() * wobbleFrequency + tentacleIndex;
                float waveAmplitude = currentWobbleAmplitude * (float)Math.Sin(t * Math.PI); // Максимум в середине

                // Направление перпендикулярное к линии между начальной и конечной точками
                float dx = endPoint.X - startPoint.X;
                float dy = endPoint.Y - startPoint.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);

                // Уменьшаем амплитуду когда скорость мыши близка к нулю
                float speedFactor =
                    (float)Math.Sqrt(mouseVelocity.X * mouseVelocity.X + mouseVelocity.Y * mouseVelocity.Y);
                speedFactor = Math.Min(1.0f, speedFactor / 10.0f);

                // Если настроены волны в состоянии покоя, не уменьшаем их амплитуду
                if (!wobbleWhileIdle)
                {
                    waveAmplitude *= speedFactor;
                }

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
                    // Без колебаний - только прямая линия с гравитацией
                    tentaclePoints[tentacleIndex][i] = basePoint;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Отрисовка щупалец
            for (int i = 0; i < SegmentCount; i++)
            {
                // Градиент от голубого к синему (небесный)
                Color startColor = Color.FromArgb(135, 206, 250); // LightSkyBlue
                Color endColor = Color.FromArgb(65, 105, 225); // RoyalBlue

                // Рисуем линии между точками
                for (int j = 0; j < PointsPerTentacle - 1; j++)
                {
                    // Интерполяция цвета
                    float t = (float)j / (PointsPerTentacle - 1);
                    Color segmentColor = InterpolateColor(startColor, endColor, t);

                    // Толщина линии в зависимости от формы щупальца
                    float thickness = GetThicknessForShape(t);

                    using (Pen pen = new Pen(segmentColor, thickness))
                    {
                        g.DrawLine(pen, tentaclePoints[i][j], tentaclePoints[i][j + 1]);
                    }
                }

                // Если форма с наконечником, рисуем круг на конце
                if (tentacleStyle == TentacleStyle.TaperedWithTip)
                {
                    Color tipColor = endColor;
                    float tipSize = 4f;
                    using (SolidBrush brush = new SolidBrush(tipColor))
                    {
                        PointF tipPoint = tentaclePoints[i][PointsPerTentacle - 1];
                        g.FillEllipse(brush, tipPoint.X - tipSize / 2, tipPoint.Y - tipSize / 2, tipSize, tipSize);
                    }
                }
            }
        }

        private float GetThicknessForShape(float t)
        {
            switch (tentacleStyle) // было currentShape
            {
                case TentacleStyle.Tapered:
                    return 5f * (1f - t * 0.7f);
                case TentacleStyle.Uniform:
                    return 3f;
                case TentacleStyle.TaperedWithTip:
                    return 5f * (1f - t * 0.8f);
                default:
                    return 5f * (1f - t * 0.7f);
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
            // Всегда генерируем точку впереди курсора
            float speed = (float)Math.Sqrt(mouseVelocity.X * mouseVelocity.X + mouseVelocity.Y * mouseVelocity.Y);
    
            // Базовое направление - вперед по ходу движения
            float dirX = speed > 0.1f ? mouseVelocity.X / speed : random.Next(-100, 100) / 100f;
            float dirY = speed > 0.1f ? mouseVelocity.Y / speed : random.Next(-100, 100) / 100f;
    
            // Угол с отклонением вперед по ходу движения (от -60° до +60°)
            double baseAngle = Math.Atan2(dirY, dirX);
            double angle = baseAngle + (random.NextDouble() - 0.5) * Math.PI * 2/3;
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

        private float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
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
