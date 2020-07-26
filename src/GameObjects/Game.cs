﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Blazor.Extensions;
using Blazor.Extensions.Canvas.Canvas2D;
using Microsoft.AspNetCore.Components;
using Blazored.LocalStorage;

namespace BlazorInvaders.GameObjects
{
    public class Game
    {
        List<Alien> _aliens;
        List<Shot> _shots;
        List<Bomb> _bombs;
        float _lastUpdate = 0.0F;
        float _removeLifeBanner = 0.0F;
        Direction _currentMovement;
        Player _player;
        Canvas2DContext _context;
        readonly GameTime _gameTime = new GameTime();
        ElementReference _spriteSheet;
        int _width;
        int _height;
        int _points;
        int _lives;
        Random _random = new Random();
        private bool _lostALife;
        private int _highScore;
        private ILocalStorageService _localStorage;
        private MotherShip _motherShip;

        public Game(int gameWidth, int gameHeight, ILocalStorageService localStorage)
        {
            _width = gameWidth;
            _height = gameHeight;
            _localStorage = localStorage;
        }

        public async ValueTask Init(BECanvasComponent canvas, ElementReference spriteSheet)
        {
            _context = await canvas.CreateCanvas2DAsync();
            _spriteSheet = spriteSheet;
            if (await _localStorage.ContainKeyAsync("BlazorInvadersHighScore"))
            {
                _highScore = await _localStorage.GetItemAsync<int>("BlazorInvadersHighScore");
            }
        }

        public void Start(float time)
        {
            _aliens = new List<Alien>();
            _shots = new List<Shot>();
            _bombs = new List<Bomb>();
            for (int i = 0; i < 11; i++)
            {
                _aliens.Add(new Alien(AlienType.Squid, new Point((i * 60 + 120), 100), i, 0));
                _aliens.Add(new Alien(AlienType.Crab, new Point((i * 60 + 120), 145), i, 1));
                _aliens.Add(new Alien(AlienType.Crab, new Point((i * 60 + 120), 190), i, 2));
                _aliens.Add(new Alien(AlienType.Octopus, new Point((i * 60 + 120), 235), i, 3));
                _aliens.Add(new Alien(AlienType.Octopus, new Point((i * 60 + 120), 270), i, 4));
            }
            Started = true;
            Won = false;
            GameOver = false;
            _lostALife = false;
            _gameTime.TotalTime = time;
            _lastUpdate = time;
            _player = new Player(_width, _height);
            _points = 0;
            _lives = 3;
        }

        public void Update(float timeStamp)
        {
            if (!Started)
            {
                return;
            }
            _gameTime.TotalTime = timeStamp;
            UpdateBombs();
            UpdateAliens();
            UpdateShots();
            if (_aliens.All(_ => _.Destroyed))
            {
                Won = true;
            }
            if (_motherShip == null && _random.Next(100) > 90)
            {
                _motherShip = new MotherShip(new Point(_width, 80));
            }
        }

        public async ValueTask Render()
        {
            await _context.ClearRectAsync(0, 0, _width, _height);
            await _context.BeginBatchAsync();
            await _context.SetFillStyleAsync("white");
            await _context.SetFontAsync("32px consolas");
            var text = $"Points: {_points.ToString("D3")} High score: {_highScore} Lives: {_lives.ToString("D3")}";
            var length = await _context.MeasureTextAsync(text);
            await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 25);
            if (GameOver)
            {
                await RenderGameOver();
            }
            else if (Won)
            {
                await RenderWonGame();
            }
            else if (Started)
            {
                await RenderGameFrame();
            }
            else if (!Started)
            {
                await _context.SetFillStyleAsync("white");
                text = $"Use arrow keys to move, space to fire";
                length = await _context.MeasureTextAsync(text);
                await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 150);
                text = $"Press space to start";
                length = await _context.MeasureTextAsync(text);
                await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 200);
            }

            await _context.EndBatchAsync();
        }

        public bool Started { get; private set; }

        public bool Won { get; private set; }

        public bool GameOver { get; private set; }

        public IEnumerable<Alien> Aliens => _aliens;

        public Player Player => _player;

        public void MovePlayer(Direction direction) => Player.Move(10, direction);

        public void Fire() =>
            _shots.Add(new Shot(new Point(_player.CurrentPosition.X + 13, Player.CurrentPosition.Y)));

        private void UpdateShots()
        {
            foreach (var s in _shots.Where(_ => !_.Remove))
            {
                s.Move();
                var destroyed = _aliens.Where(_ => !_.Destroyed && !_.HasBeenHit).OrderByDescending(_ => _.CurrentPosition.Y).FirstOrDefault(_ => _.Collision(s));
                if (destroyed != null)
                {
                    s.Remove = true;
                    _points += 100;
                }
                if (_motherShip != null)
                {
                    if (_motherShip.Collision(s))
                    {
                        _points += 1000;
                        _motherShip = null;
                    }
                }
            }

        }

        private void UpdateAliens()
        {
            if (_gameTime.TotalTime - _lastUpdate > 250)
            {
                _lastUpdate = _gameTime.TotalTime;

                if (_aliens.Any(_ => (_.CurrentPosition.X + 80) >= (_width - 10)) && _currentMovement == Direction.Right)
                {
                    _aliens.ForEach(_ => _.MoveDown());
                    _currentMovement = Direction.Left;
                }
                else if (_aliens.Any(_ => _.CurrentPosition.X <= 15) && _currentMovement == Direction.Left)
                {
                    _aliens.ForEach(_ => _.MoveDown());
                    _currentMovement = Direction.Right;
                }
                else
                {
                    _aliens.ForEach(_ => _.MoveHorizontal(_currentMovement));
                }
                _aliens.Where(_ => _.HasBeenHit).ToList().ForEach(_ => _.Destroyed = true);
                if (_aliens.Any(_ => _player.Collision(_)))
                {
                    _lives = 0;
                    GameOver = true;
                }
            }
        }

        private void UpdateBombs()
        {
            if ((!_bombs.Any() || _bombs.All(_ => _.Remove)) && _random.Next(100) > (75 + (_aliens.Count(_ => _.Destroyed) / 2)))
            {
                var xColumn = _random.Next(0, 10);
                var alien = _aliens.OrderByDescending(_ => _.CurrentPosition.Y).FirstOrDefault(_ => _.Column == xColumn && !_.Destroyed);
                var counter = 0;
                while (alien == null && counter < 60)
                {
                    xColumn = _random.Next(0, 10);
                    alien = _aliens.OrderByDescending(_ => _.CurrentPosition.Y).FirstOrDefault(_ => _.Column == xColumn && !_.Destroyed);
                    counter++;
                }
                _bombs.Add(new Bomb(new Point(alien.CurrentPosition.X + alien.Sprite.RenderSize.Width / 2, alien.CurrentPosition.Y)));
            }
            foreach (var b in _bombs.Where(_ => !_.Remove))
            {
                b.Move();
                if (_player.Collision(b))
                {
                    _lives--;
                    GameOver = _lives < 1;
                    _lostALife = true;
                    b.Remove = true;
                    _removeLifeBanner = _gameTime.TotalTime;
                }
            }
            var x = _gameTime.TotalTime;
            if (_lostALife && _gameTime.TotalTime - _removeLifeBanner > 1000)
            {
                _lostALife = false;
            }
        }

        private async Task RenderGameOver()
        {
            var text = $"Game over, you lost!";
            var length = await _context.MeasureTextAsync(text);
            await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 150);
            text = $"Press space to start a new game";
            length = await _context.MeasureTextAsync(text);
            await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 200);
            Started = false;
        }

        private async Task RenderWonGame()
        {
            var text = $"Congratulations you won!";
            var length = await _context.MeasureTextAsync(text);
            await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 150);
            text = $"Press space to start a new game";
            length = await _context.MeasureTextAsync(text);
            await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 200);
            Started = false;
            if (_points > _highScore)
            {
                _highScore = _points;
                await _localStorage.SetItemAsync<int>("BlazorInvadersHighScore", _highScore);
            }
        }

        private async Task RenderGameFrame()
        {
            if (_lostALife)
            {
                var text = $"You where hit, lost a life!";
                var length = await _context.MeasureTextAsync(text);
                await _context.SetFillStyleAsync("red");
                await _context.FillTextAsync(text, _width / 2 - (length.Width / 2), 55);
                await _context.SetFillStyleAsync("white");
            }
            foreach (var a in Aliens)
            {
                if (a.Destroyed)
                {
                    continue;
                }
                await _context.DrawImageAsync(_spriteSheet,
                    a.Sprite.TopLeft.X, a.Sprite.TopLeft.Y, a.Sprite.Size.Width, a.Sprite.Size.Height, a.CurrentPosition.X, a.CurrentPosition.Y, a.Sprite.RenderSize.Width, a.Sprite.RenderSize.Height);
            }
            var ship = Player.Sprite;
            await _context.DrawImageAsync(_spriteSheet,
                ship.TopLeft.X, ship.TopLeft.Y, 18, 12, Player.CurrentPosition.X, Player.CurrentPosition.Y, 36, 24); //TODO: Remove hardcoded sizes
            foreach (var s in _shots.Where(_ => !_.Remove))
            {
                if (s.CurrentPosition.Y <= 0)
                {
                    s.Remove = true;
                    continue;
                }
                await _context.DrawImageAsync(_spriteSheet,
                    s.Sprite.TopLeft.X, s.Sprite.TopLeft.Y, 20, 14, s.CurrentPosition.X, s.CurrentPosition.Y, 40, 28); //TODO: Remove hardcoded sizes
            }
            foreach (var b in _bombs.Where(_ => !_.Remove))
            {
                if (b.CurrentPosition.Y >= _height)
                {
                    b.Remove = true;
                    continue;
                }
                var x = b.Sprite.Size;
                var y = b.Sprite.RenderSize;
                await _context.DrawImageAsync(_spriteSheet,
                    b.Sprite.TopLeft.X, b.Sprite.TopLeft.Y, b.Sprite.Size.Width, b.Sprite.Size.Height, b.CurrentPosition.X, b.CurrentPosition.Y, b.Sprite.RenderSize.Width, b.Sprite.RenderSize.Height);
            }
            if (_motherShip != null)
            {
                _motherShip.Move();
                if (_motherShip.CurrentPosition.X < 0)
                {
                    _motherShip = null;
                }
                else
                {
                    await _context.DrawImageAsync(_spriteSheet,
                   _motherShip.Sprite.TopLeft.X, _motherShip.Sprite.TopLeft.Y, _motherShip.Sprite.Size.Width, _motherShip.Sprite.Size.Height, _motherShip.CurrentPosition.X, _motherShip.CurrentPosition.Y, _motherShip.Sprite.RenderSize.Width, _motherShip.Sprite.RenderSize.Height); //TODO: Remove hardcoded sizes
                }
            }
        }
    }
}