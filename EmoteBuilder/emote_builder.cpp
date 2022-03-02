#include <QFileDialog>
#include <QStackedLayout>
#include <QStandardPaths>
#include "emote_builder.hpp"
#include "logger.hpp"
#include "./ui_emote_builder.h"

QList<QPoint> EmoteBuilder::anchors;
QMap<QString, QImage> EmoteBuilder::frames;
int EmoteBuilder::fps;

EmoteBuilder::EmoteBuilder(QWidget *parent)
    : QMainWindow(parent)
    , ui(new Ui::EmoteBuilder)
{
    ui->setupUi(this);

    QString localDir = QStandardPaths::locate(QStandardPaths::GenericDataLocation, nullptr, QStandardPaths::LocateDirectory);
    Logger::open(localDir + "EmoteBuilder/Log.txt");

    builder = new Builder(4096, 4096, 1, true, false, true);

    QStackedLayout *stackedView = new QStackedLayout();
    ui->viewLayout->addLayout(stackedView);
    stackedView->setStackingMode(QStackedLayout::StackAll);
    stackedView->addWidget(ui->spriteView);
    stackedView->addWidget(ui->referenceView);
    stackedView->setAlignment(Qt::AlignCenter);

    connect(&currentAnimation, &SpriteAnimation::frameNumberChanged, this, &EmoteBuilder::updateFrameDisplay);
    connect(&currentAnimation, &SpriteAnimation::frameChanged, this, &EmoteBuilder::updatePixmap);
}

EmoteBuilder::~EmoteBuilder()
{
    Logger::close();

    delete ui;
}

void EmoteBuilder::updateFrameDisplay(int frameNumber)
{
    ui->currentFrameInput->setText(QString::number(frameNumber));
}

void EmoteBuilder::updatePixmap(QPixmap pixmap)
{
    ui->spriteView->setPixmap(pixmap);
}

QImage trimImage(QImage image)
{
    int width = image.width();
    int height = image.height();
    int top = height / 2;
    int bottom = top;
    int left = width / 2 ;
    int right = left;
    for (int x = 0; x < width; x++) {
        for (int y = 0; y < height; y++) {
            if (image.pixelColor(x, y).alpha() != 0){
                top    = std::min(top, y);
                bottom = std::max(bottom, y);
                left   = std::min(left, x);
                right  = std::max(right, x);
            }
        }
    }

    return image.copy(left, top, right - left + 1, bottom - top + 1);
}

void EmoteBuilder::on_loadSpritesButton_clicked()
{
    anchors.clear();
    frames.clear();

    QStringList imagePaths = QFileDialog::getOpenFileNames(Q_NULLPTR, "Select sprites", Q_NULLPTR, "*.png");
    for (QString imagePath : imagePaths)
    {
        QImage frame(imagePath);
        QImage trimmedFrame = trimImage(frame);
        QFileInfo imageInfo(imagePath);
        frames.insert(imageInfo.baseName(), trimmedFrame);
        anchors.append(QPoint(0, 0));
    }

    if (frames.count() <= 0) return;

    ui->promptLabel->hide();
    ui->buildAtlasButton->setEnabled(true);

    QList<QPixmap> pixmaps;
    for (auto frame : frames.values())
    {
        pixmaps.append(QPixmap::fromImage(frame));
    }

    if (currentAnimation.isPlaying())
    {
        currentAnimation.stop();
    }

    bool validFPS;
    int fps = ui->fpsInput->displayText().toInt(&validFPS);
    currentAnimation.init(validFPS ? fps : 12, pixmaps, 0);
    currentAnimation.play();
}

void EmoteBuilder::on_buildAtlasButton_clicked()
{
    bool validFPS;
    int _fps = ui->fpsInput->displayText().toInt(&validFPS);
    fps = validFPS ? _fps : 12;
    builder->run();
}


void EmoteBuilder::on_playButton_clicked()
{
    if (currentAnimation.isEmpty()) return;

    if (currentAnimation.isPlaying())
    {
        currentAnimation.stop();
    }

    QList<QPixmap> pixmaps;
    for (auto frame : frames.values())
    {
        pixmaps.append(QPixmap::fromImage(frame));
    }

    bool validFPS;
    int fps = ui->fpsInput->displayText().toInt(&validFPS);
    currentAnimation.init(validFPS ? fps : 12, pixmaps, 0);
    currentAnimation.play();
}


void EmoteBuilder::on_stopButton_clicked()
{
    if (!currentAnimation.isEmpty())
    {
        currentAnimation.stop();
    }
}


void EmoteBuilder::on_prevFrameButton_clicked()
{
    if (currentAnimation.getFrameCount() <= 0) return;

    if (currentAnimation.isPlaying())
    {
        currentAnimation.stop();
    }

    int currentFrameNumber = currentAnimation.getCurrentFrameNumber() - 1;
    if (currentFrameNumber >= 0)
    {
        currentAnimation.changeFrame(currentFrameNumber);
    }
    else
    {
        currentFrameNumber = 0;
    }

    if (currentFrameNumber < anchors.count())
    {
        ui->anchorXInput->setText(QString::number(anchors[currentFrameNumber].x()));
        ui->anchorYInput->setText(QString::number(anchors[currentFrameNumber].y()));
    }
}


void EmoteBuilder::on_nextFrameButton_clicked()
{
    if (currentAnimation.getFrameCount() <= 0) return;

    if (currentAnimation.isPlaying())
    {
        currentAnimation.stop();
    }

    int currentFrameNumber = currentAnimation.getCurrentFrameNumber() + 1;
    if (currentFrameNumber < currentAnimation.getFrameCount())
    {
        currentAnimation.changeFrame(currentFrameNumber);
    }
    else
    {
        currentFrameNumber = currentAnimation.getFrameCount() - 1;
    }

    if (currentFrameNumber < anchors.count())
    {
        ui->anchorXInput->setText(QString::number(anchors[currentFrameNumber].x()));
        ui->anchorYInput->setText(QString::number(anchors[currentFrameNumber].y()));
    }
}


void EmoteBuilder::on_anchorXInput_textChanged(const QString &value)
{
    int currentFrameNumber = currentAnimation.getCurrentFrameNumber();

    bool ok;

    int anchorX = value.toInt(&ok);
    if (ok && !anchors.isEmpty())
    {
        anchors[currentFrameNumber].setX(anchorX);
    }
}


void EmoteBuilder::on_anchorYInput_textChanged(const QString &value)
{
    int currentFrameNumber = currentAnimation.getCurrentFrameNumber();

    bool ok;

    int anchorY = value.toInt(&ok);
    if (ok && !anchors.isEmpty())
    {
        anchors[currentFrameNumber].setY(anchorY);
    }
}

