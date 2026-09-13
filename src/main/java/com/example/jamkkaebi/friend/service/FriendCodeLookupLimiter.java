package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.exception.TooManyRequestsException;
import com.example.jamkkaebi.global.time.GameClock;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.temporal.ChronoUnit;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 친구 코드 조회 한도 — 사용자당 분당 10회, 하루 100회.
 *
 */
@Component
public class FriendCodeLookupLimiter {

    private final GameClock gameClock;
    private final FriendProperties properties;
    private final Map<Long, Usage> usages = new ConcurrentHashMap<>();

    public FriendCodeLookupLimiter(GameClock gameClock, FriendProperties properties) {
        this.gameClock = gameClock;
        this.properties = properties;
    }

    /**
     * 한도 +1. 한도를 넘었으면 쓰지 않고 거절한다.
     *
     * @throws TooManyRequestsException {@code F002}. 다시 시도할 수 있기까지의 초를 담는다.
     */
    public void acquire(Long userId) {
        LocalDateTime now = gameClock.now();
        LocalDateTime minute = now.truncatedTo(ChronoUnit.MINUTES);
        LocalDate day = now.toLocalDate();
        LocalDateTime[] blockedUntil = new LocalDateTime[1];

        usages.compute(userId, (id, previous) -> {
            Usage usage = previous == null ? Usage.start(minute, day) : previous.rollTo(minute, day);
            if (usage.dayCount() >= properties.lookupPerDay()) {
                blockedUntil[0] = gameClock.nextResetAt();
                return usage;
            }
            if (usage.minuteCount() >= properties.lookupPerMinute()) {
                blockedUntil[0] = minute.plusMinutes(1);
                return usage;
            }
            return usage.increment();
        });

        if (blockedUntil[0] != null) {
            long millis = Duration.between(now, blockedUntil[0]).toMillis();
            throw new TooManyRequestsException(ErrorCode.FRIEND_CODE_LOOKUP_LIMITED, (millis + 999) / 1000);
        }
    }

    // 지난 날짜의 기록을 비운다.
    @Scheduled(fixedDelay = 60 * 60 * 1000L, initialDelay = 60 * 60 * 1000L)
    public void forgetPastDays() {
        LocalDate today = gameClock.today();
        usages.values().removeIf(usage -> usage.day().isBefore(today));
    }

    private record Usage(LocalDateTime minute, int minuteCount, LocalDate day, int dayCount) {

        static Usage start(LocalDateTime minute, LocalDate day) {
            return new Usage(minute, 0, day, 0);
        }

        // 분·날짜가 바뀌었으면 해당 카운트를 0으로 넘긴다.
        Usage rollTo(LocalDateTime currentMinute, LocalDate currentDay) {
            return new Usage(
                    currentMinute,
                    minute.equals(currentMinute) ? minuteCount : 0,
                    currentDay,
                    day.equals(currentDay) ? dayCount : 0);
        }

        Usage increment() {
            return new Usage(minute, minuteCount + 1, day, dayCount + 1);
        }
    }
}
