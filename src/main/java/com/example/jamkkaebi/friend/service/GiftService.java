package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.config.FriendProperties;
import com.example.jamkkaebi.friend.domain.GiftLog;
import com.example.jamkkaebi.friend.domain.GiftState;
import com.example.jamkkaebi.friend.dto.response.GiftResponse;
import com.example.jamkkaebi.friend.repository.GiftLogRepository;
import com.example.jamkkaebi.friend.repository.UserCount;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.util.Collection;
import java.util.HashMap;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;

/**
 * 상자 선물.
 *
 * <p>하루는 자정에 바뀐다. 받은 상자권도 <b>받은 날에만</b> 쓸 수 있다 — 넘어가게 두면 선물을 모아 두었다 한 번에 여는 경로가 생긴다.
 */
@Service
public class GiftService {

    private final FriendUsers friendUsers;
    private final SocialGraph socialGraph;
    private final GiftLogRepository giftLogRepository;
    private final UserRepository userRepository;
    private final FriendProperties properties;
    private final GameClock gameClock;

    public GiftService(FriendUsers friendUsers,
                       SocialGraph socialGraph,
                       GiftLogRepository giftLogRepository,
                       UserRepository userRepository,
                       FriendProperties properties,
                       GameClock gameClock) {
        this.friendUsers = friendUsers;
        this.socialGraph = socialGraph;
        this.giftLogRepository = giftLogRepository;
        this.userRepository = userRepository;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    // 상자 선물
    @Transactional
    public GiftResponse send(String myFriendCode, String rawFriendCode) {
        User me = friendUsers.me(myFriendCode);
        User friend = friendUsers.findByCode(rawFriendCode)
                .filter(other -> !other.getId().equals(me.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.FRIEND_NOT_FOUND));
        friendUsers.lock(me.getId(), friend.getId());
        if (!socialGraph.areFriends(me.getId(), friend.getId())) {
            throw new BusinessException(ErrorCode.FRIEND_NOT_FOUND);
        }

        LocalDate today = gameClock.today();
        Optional<GiftLog> sentToday = giftLogRepository.findByFromUserIdAndSentDate(me.getId(), today);
        if (sentToday.isPresent()) {
            // 같은 친구에게 다시 온 요청은 응답을 못 받고 재시도한 경우라 성공으로 돌려준다.
            if (sentToday.get().getToUserId().equals(friend.getId())) {
                return new GiftResponse(0, GiftState.SENT);
            }
            throw new BusinessException(ErrorCode.GIFT_ALREADY_SENT_TODAY);
        }

        long receivedToday = giftLogRepository.countByToUserIdAndSentDate(friend.getId(), today);
        if (receivedToday >= properties.giftReceiveLimit()) {
            throw new BusinessException(ErrorCode.GIFT_RECEIVER_FULL);
        }

        boolean converted = receivedToday >= 1;
        giftLogRepository.save(GiftLog.builder()
                .fromUserId(me.getId())
                .toUserId(friend.getId())
                .sentDate(today)
                .converted(converted)
                .build());

        int reward = properties.giftRewardJeongseong();
        userRepository.addJeongseong(me.getId(), reward);
        if (converted) {
            userRepository.addJeongseong(friend.getId(), reward);
        }
        return new GiftResponse(reward, GiftState.SENT);
    }

    // 친구별 선물 버튼 상태
    @Transactional(readOnly = true)
    public Map<Long, GiftState> giftStates(Long myId, Collection<Long> friendIds, LocalDate today) {
        if (friendIds.isEmpty()) {
            return Map.of();
        }
        Optional<Long> sentTo = giftLogRepository.findByFromUserIdAndSentDate(myId, today)
                .map(GiftLog::getToUserId);
        Map<Long, Long> receivedCounts = giftLogRepository.countReceivedByUsers(friendIds, today).stream()
                .collect(Collectors.toMap(UserCount::userId, UserCount::count));

        Map<Long, GiftState> states = new HashMap<>();
        for (Long friendId : friendIds) {
            states.put(friendId, stateFor(friendId, sentTo, receivedCounts.getOrDefault(friendId, 0L)));
        }
        return states;
    }

    @Transactional(readOnly = true)
    public boolean sentToday(Long myId, LocalDate today) {
        return giftLogRepository.findByFromUserIdAndSentDate(myId, today).isPresent();
    }

    // 오늘 받은 선물 중 아직 쓰지 않은 상자권이 있는가.
    @Transactional(readOnly = true)
    public boolean ticketAvailable(Long myId, LocalDate today) {
        return giftLogRepository.existsByToUserIdAndSentDateAndConvertedFalseAndClaimedFalse(myId, today);
    }

    private GiftState stateFor(Long friendId, Optional<Long> sentTo, long receivedToday) {
        if (sentTo.isPresent()) {
            return sentTo.get().equals(friendId) ? GiftState.SENT : GiftState.DONE_TODAY;
        }
        return receivedToday >= properties.giftReceiveLimit() ? GiftState.RECEIVER_FULL : GiftState.AVAILABLE;
    }
}
