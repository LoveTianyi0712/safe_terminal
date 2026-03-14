package com.safeterminal.security;

import com.safeterminal.repository.SysUserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.core.userdetails.User;
import org.springframework.security.core.userdetails.UserDetails;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.security.core.userdetails.UsernameNotFoundException;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

@Service
@RequiredArgsConstructor
public class UserDetailsServiceImpl implements UserDetailsService {

    private final SysUserRepository sysUserRepository;

    @Override
    @Transactional(readOnly = true)
    public UserDetails loadUserByUsername(String username) throws UsernameNotFoundException {
        var sysUser = sysUserRepository.findByUsername(username)
                .orElseThrow(() -> new UsernameNotFoundException("User not found: " + username));

        // Spring Security 的角色需要带 ROLE_ 前缀才能配合 hasRole() 使用
        var authority = new SimpleGrantedAuthority("ROLE_" + sysUser.getRole());

        return User.builder()
                .username(sysUser.getUsername())
                .password(sysUser.getPassword())
                .authorities(List.of(authority))
                .disabled(!sysUser.getEnabled())
                .build();
    }
}
