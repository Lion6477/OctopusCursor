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
        // Параметры «тентакля»
        private const int SegmentCount = 15;      // Количество сегментов
        private const float SegmentLength = 30f;  // Длина между сегментами
        private const float SmoothSpeed = 0.15f;  // Насколько быстро сегменты "догоняют"
        private const float WobbleAmplitude = 10f;// Амплитуда «волнения»
        private const float WobbleFrequency = 2f; // Частота «волнения»

        private PointF[] segments; // Позиции звеньев
        private Timer updateTimer; // Таймер для обновления ~60 FPS

        public TentacleForm()
        {
            // ========= Настройки формы =========
            // Без рамки, на весь экран, поверх всех окон
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            
            // Делаем фон "ярко-зелёным" и прозрачным
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            
            // Не показывать в панели задач
            this.ShowInTaskbar = false;

            // Включим двойную буферизацию, чтобы уменьшить мерцание
            this.DoubleBuffered = true;

            // Инициализируем сегменты (все в начальной точке курсора)
            Point startPos = Cursor.Position;
            segments = new PointF[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                segments[i] = startPos;
            }

            // Создаём таймер на ~60 FPS (16 мс)
            updateTimer = new Timer();
            updateTimer.Interval = 16;
            updateTimer.Tick += UpdateTentacle;
            updateTimer.Start();
        }

        // Переопределяем стили окна, чтобы клик «проходил» насквозь (WS_EX_TRANSPARENT)
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20;     // WS_EX_TRANSPARENT
                cp.ExStyle |= 0x80000;  // WS_EX_LAYERED (для прозрачности)
                return cp;
            }
        }

        private void UpdateTentacle(object sender, EventArgs e)
        {
            // Позиция курсора в глобальных координатах экрана
            PointF mousePos = Cursor.Position;

            // Все сегменты теперь начинаются от курсора
            for (int i = 0; i < SegmentCount; i++)
            {
                // Рассчитываем угол для равномерного распределения сегментов
                float angle = (float)(i * 2 * Math.PI / SegmentCount);
        
                // Добавляем шум к углу для естественного движения
                float noiseAngle = angle + (float)Math.Sin((GetTime() + i) * WobbleFrequency) * 0.5f;
        
                // Вычисляем направление
                float dx = (float)Math.Cos(noiseAngle);
                float dy = (float)Math.Sin(noiseAngle);
        
                // Длина сегмента пропорциональна его номеру
                float length = SegmentLength * (i + 1);
        
                // Целевая позиция для i-го сегмента
                float targetX = mousePos.X + dx * length;
                float targetY = mousePos.Y + dy * length;
        
                // Плавное приближение к целевой позиции
                PointF target = new PointF(targetX, targetY);
                segments[i] = Lerp(segments[i], target, SmoothSpeed);
            }

            // Запрос перерисовки
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Получаем текущую позицию курсора для рисования
            PointF mousePos = Cursor.Position;

            // Рисуем каждый сегмент от курсора
            using (Pen pen = new Pen(Color.Blue, 3f))
            {
                for (int i = 0; i < SegmentCount; i++)
                {
                    e.Graphics.DrawLine(pen, mousePos, segments[i]);
                }
            }
        }

        // Линейная интерполяция между двумя точками
        private PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t
            );
        }

        // Простая функция, считающая «время с начала работы процесса» (в секундах)
        private float GetTime()
        {
            return (float)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        }
    }
}
