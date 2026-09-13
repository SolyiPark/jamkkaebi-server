package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.repository.FriendRecommendationRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.GiftLogRepository;
import com.example.jamkkaebi.friend.repository.VisitLogRepository;
import com.example.jamkkaebi.global.time.GameClock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;

/**
 * 저장 공간과 인덱스를 위해 친구 기능이 매일 쌓는 행을 정리한다.
 *
 */
@Component
public class FriendCleanupScheduler {

    private static final Logger log = LoggerFactory.getLogger(FriendCleanupScheduler.class);

    private static final long INTERVAL_MS = 60 * 60 * 1000L;

    private static final int LOG_RETENTION_DAYS = 30;

    private final FriendRequestRepository friendRequestRepository;
    private final FriendRecommendationRepository recommendationRepository;
    private final GiftLogRepository giftLogRepository;
    private final VisitLogRepository visitLogRepository;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public FriendCleanupScheduler(FriendRequestRepository friendRequestRepository,
                                  FriendRecommendationRepository recommendationRepository,
                                  GiftLogRepository giftLogRepository,
                                  VisitLogRepository visitLogRepository,
                                  FriendProperties properties,
                                  GameClock gameClock) {
        this.friendRequestRepository = friendRequestRepository;
        this.recommendationRepository = recommendationRepository;
        this.giftLogRepository = giftLogRepository;
        this.visitLogRepository = visitLogRepository;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    @Scheduled(fixedDelay = INTERVAL_MS, initialDelay = INTERVAL_MS)
    @Transactional
    public void purgeExpired() {
        LocalDate today = gameClock.today();
        int requests = friendRequestRepository.deleteExpired(gameClock.now());
        // 추천은 최근 7일간 추천한 사람 제외 판정에 쓰이므로 그 기간은 남긴다.
        int recommendations = recommendationRepository.deleteOlderThan(
                today.minusDays(properties.recommendationCooldownDays()));
        int gifts = giftLogRepository.deleteOlderThan(today.minusDays(LOG_RETENTION_DAYS));
        int visits = visitLogRepository.deleteOlderThan(today.minusDays(LOG_RETENTION_DAYS));
        if (requests > 0 || recommendations > 0 || gifts > 0 || visits > 0) {
            log.debug("만료된 친구 요청 {}건, 지난 추천 {}건, 선물 기록 {}건, 방문 기록 {}건을 정리했습니다.",
                    requests, recommendations, gifts, visits);
        }
    }
}
