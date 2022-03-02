#include "sprite_animation.hpp"
#include "logger.hpp"

SpriteAnimation::SpriteAnimation(QObject* parent) : QObject(parent)
{
	fps = 12.0f;
    frameNumber = 0;
	loopStart = 0;
    frames.clear();

    connect(&frameTimer, &QTimer::timeout, this, &SpriteAnimation::advanceFrame);
}

SpriteAnimation::~SpriteAnimation()
{
    frameTimer.stop();
}

int SpriteAnimation::getCurrentFrameNumber()
{
    return frameNumber;
}

int SpriteAnimation::getFrameCount()
{
    return frames.count();
}

void SpriteAnimation::init(float fps, QList<QPixmap> frames, int loopStart)
{
    currentFrame = frames.at(0);
    this->fps = fps;
    frameNumber = 0;
    this->frames = frames;
    this->loopStart = loopStart < frames.length() ? loopStart : 0;
    changeFrame(frameNumber);
}

bool SpriteAnimation::isEmpty()
{
    return frames.empty();
}

bool SpriteAnimation::isPlaying()
{
    return frameTimer.isActive();
}

void SpriteAnimation::play(int fromFrame)
{
    changeFrame(fromFrame);
    frameTimer.start(1000 / fps);
}

void SpriteAnimation::stop(int atFrame)
{
	changeFrame(atFrame);
    frameTimer.stop();
}

void SpriteAnimation::advanceFrame()
{
	changeFrame(frameNumber + 1);
}

void SpriteAnimation::changeFrame(int frameNumber)
{
    this->frameNumber = frameNumber % frames.count();
    currentFrame = frames.at(this->frameNumber);
	emit frameChanged(currentFrame);
    emit frameNumberChanged(this->frameNumber);
}
