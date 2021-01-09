using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfHelper.Commands;

namespace AIS
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Fields

        const int PERIOD = 60;

        const int INITIAL_INFECTEE = 5; // 초기 감염자 수입니다. Number of initial infected people.
        const int POPULATION = 10000; // 총 인구수입니다. Total population of group.
        const double MOVING_DIST_MEAN = 0.001; // 각 사람의 이동 거리에 대한 정규분포의 평균입니다. Mean for normal distribution of each person's moving distance.
        const double MOVING_DIST_STD = 0.003; // 각 사람의 이동 거리에 대한 정규분포의 표준편차입니다. Standard deviation for normal distrubution of each person's moving distance.
        const double INFECTION_DIST = 0.01; // 감염되는 최소한의 거리입니다. Minimum distance of infection.
        const double INFECTION_DIST_SQUARE = INFECTION_DIST * INFECTION_DIST; // 감염되는 최소한의 거리의 제곱입니다(계산 효율 향상을 위함). Minimum distance square of infection (for improve calculating performance).
        const double INFECTION_PROB = 0.2; // INFECTION_DIST 내에서 감염될 확률입니다. Probability of infection when each person nears the distance below INFECTION_DIST.

        private static readonly SKPaint _redPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Red
        };
        private static readonly SKPaint _blackPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black
        };

        private readonly Random _random = new Random();

        private Queue<long> _ticks = new Queue<long>();
        private long _tick = 0;

        private Person[] _people = new Person[POPULATION];
        private double[] _peopleX = new double[POPULATION];

        private bool _isLoaded = false;

        #endregion

        #region Properties

        public int Population => POPULATION;
        public int Infected { get; set; }

        public int FPS { get; set; }
        public bool Running { get; set; }

        #endregion

        #region Commands

        public ICommand LoadPopulationCommand { get; set; }
        private void LoadPopulation()
        {
            ResetParameters();
            _isLoaded = true;
            skElement.InvalidateVisual();
        }
        private bool CanLoadPopulation()
        {
            return true;
        }

        public ICommand RunSimulationCommand { get; set; }
        private void RunSimulation()
        {
            Running = !Running;
            OnPropertyChanged("Running");

            if (Running)
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            else
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                _ticks.Clear();
            }
        }

        private bool CanRunSimulation()
        {
            return _isLoaded;
        }

        #endregion

        #region Functions and Voids

        private void ResetParameters()
        {
            _ticks.Clear();
            _tick = 0L;

            Infected = INITIAL_INFECTEE;
            OnPropertyChanged("Infected");

            for (int i = 0; i < Population; ++i)
                _people[i] = new Person(_random.NextDouble(), _random.NextDouble());
            foreach (int idx in _random.Permutation(Population, Infected))
                _people[idx - 1].Infected = true;
        }

        private void CalculateFPS()
        {
            var endTime = DateTime.Now.Ticks;
            var startTime = endTime - 10000000;
            while (_ticks.Any())
            {
                if (_ticks.Peek() < startTime)
                {
                    _ticks.Dequeue();
                    continue;
                }

                break;
            }

            _ticks.Enqueue(endTime);

            if (_tick % PERIOD == 0)
            {
                FPS = _ticks.Count;
                OnPropertyChanged("FPS");
            }
        }

        private void SetTendency()
        {
            for (int i = 0; i < Population; ++i)
            {
                double magnitude = Math.Abs(_random.NextGaussian(MOVING_DIST_MEAN, MOVING_DIST_STD));
                double angle = _random.NextDouble() * 2 * Math.PI;

                _people[i].Vx = magnitude * Math.Cos(angle);
                _people[i].Vy = magnitude * Math.Sin(angle);
            }
        }

        private void MovePeople()
        {
            for (int i = 0; i < Population; ++i)
            {
                var pivot = _people[i];
                _people[i].X += pivot.Vx;
                _people[i].Y += pivot.Vy;

                if (pivot.X < 0.0 && pivot.Vx < 0)
                {
                    _people[i].X = -pivot.X;
                    _people[i].Vx = -pivot.Vx;
                }
                else if (pivot.X > 1 && pivot.Vx > 0)
                {
                    _people[i].X = 2.0 - pivot.X;
                    _people[i].Vx = -pivot.Vx;
                }

                if (pivot.Y < 0.0 && pivot.Vy < 0)
                {
                    _people[i].Y = -pivot.Y;
                    _people[i].Vy = -pivot.Vy;
                }
                else if (pivot.Y > 1 && pivot.Vy > 0)
                {
                    _people[i].Y = 2.0 - pivot.Y;
                    _people[i].Vy = -pivot.Vy;
                }
            }
        }

        private void CalculateInfection() // O(nlog n)
        {
            // 정렬 순서: 1. 감염인이 비감염인보다 우선으로 2. X좌표가 오름차순으로
            // Sorting order: 1. noninfective first 2. ascending x-coords order
            Array.Sort(_people, (p1, p2) => p1.Infected != p2.Infected ? p2.Infected.CompareTo(p1.Infected) : p1.X.CompareTo(p2.X));
            int firstNoninfectiveOccurIndex = Infected;

            // 왜 LINQ를 쓰지 않습니까? Why do not using Linq?
            // 새로운 인스턴스 생성으로 인한 GC의 발생을 막기 위함. Avoid to invoke GC from creating new instance.
            for (int i = 0; i < POPULATION; ++i)
                _peopleX[i] = _people[i].X;

            for (int i = 0; i < firstNoninfectiveOccurIndex; ++i) // 감염인 대상 이웃 탐색. Search neighbor nears infectee.
            {
                var infecteePivot = _people[i];
                
                int rangeLeft = Array.BinarySearch(_peopleX, firstNoninfectiveOccurIndex, POPULATION - firstNoninfectiveOccurIndex, infecteePivot.X - INFECTION_DIST);
                if (rangeLeft < 0)
                    rangeLeft = ~rangeLeft;
                int rangeRight = Array.BinarySearch(_peopleX, firstNoninfectiveOccurIndex, POPULATION - firstNoninfectiveOccurIndex, infecteePivot.X + INFECTION_DIST);
                if (rangeRight < 0)
                    rangeRight = ~rangeRight;

                for (int j = rangeLeft; j < rangeRight; ++j)
                {
                    var adjacencyPivot = _people[j];
                    if (adjacencyPivot.Infected)
                        continue;

                    double x = adjacencyPivot.X - infecteePivot.X;
                    double y = adjacencyPivot.Y - infecteePivot.Y;
                    if (_random.NextDouble() < INFECTION_PROB && x * x + y * y < INFECTION_DIST_SQUARE)
                    {
                        _people[j].Infected = true;
                        Infected++;
                    }
                }
            }

            OnPropertyChanged("Infected");
        }

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            if (_tick % PERIOD == 0)
                SetTendency();
            MovePeople();
            if (Infected != Population)
                CalculateInfection();

            skElement.InvalidateVisual();

            _tick++;
            CalculateFPS();
        }

        private void skElement_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            if (!_isLoaded)
                return;

            var info = e.Info;
            var surface = e.Surface;
            var canvas = surface.Canvas;

            canvas.Clear();

            for (int i = 0; i < Infected; ++i) // 감염인 infectee
            {
                var infecteePivot = _people[i];
                canvas.DrawCircle(info.Width * (float)infecteePivot.X, info.Height * (float)infecteePivot.Y, 2, _redPaint);
            }
            for (int i = Infected; i < POPULATION; ++i) // 비감염인 non-infective
            {
                var noninfectivePivot = _people[i];
                canvas.DrawCircle(info.Width * (float)noninfectivePivot.X, info.Height * (float)noninfectivePivot.Y, 2, _blackPaint);
            }
        }

        #endregion

        #region Constructor

        public MainWindow()
        {
            LoadPopulationCommand = new RelayCommand(LoadPopulation, CanLoadPopulation);
            RunSimulationCommand = new RelayCommand(RunSimulation, CanRunSimulation);
            InitializeComponent();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
